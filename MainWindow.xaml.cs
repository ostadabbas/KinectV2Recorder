//------------------------------------------------------------------------------
// <copyright file="MainWindow.xaml.cs" company="Microsoft">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

namespace Microsoft.Samples.Kinect.DepthBasics
{
    using System;
    using System.Globalization;
    using System.IO;
    using System.Windows;
    using System.Windows.Media;
    using System.Windows.Media.Imaging;
    using Microsoft.Kinect;
    using System.Threading.Tasks;
    using System.Diagnostics;
    using AForge.Video.FFMPEG;
    using System.Drawing;

    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        /// <summary>
        /// Active Kinect sensor
        /// </summary>
        private KinectSensor kinectSensor = null;

        /// <summary>
        /// Reader for depth frames
        /// </summary>
        private MultiSourceFrameReader frameReader = null;

        /// <summary>
        /// Description of the data contained in the depth frame
        /// </summary>
        private FrameDescription depthFrameDescription = null;
        private FrameDescription colorFrameDescription = null;

        /// <summary>
        /// Bitmap to display
        /// </summary>
        private WriteableBitmap depthBitmap = null;
        private WriteableBitmap colorBitmap = null;

        /// <summary>
        /// Intermediate storage for frame data
        /// </summary>
        private byte[] depthPixels = null;
        private ushort[] depthValues = null;
        private byte[] colorPixels = null;
        private byte[] processColorArr = null;

        //Initialize some global variables
        private int FrameCounter = 0;
        private Stopwatch timer = new Stopwatch();
        private TimeSpan frameTime = new TimeSpan(0,0,0);
        private TimeSpan procTime = new TimeSpan(0, 0, 0);
        private const double GM_PCT = 0.96;
        private const double PROC_CUTOFF = 0.8;

        private bool IsRecording = false;
        private BinaryWriter writer;
        private static int fps = 1;
        private bool extAvail = false;
        private double levelAvg = 0;

        private VideoFileWriter colorWriter = null;

        /// <summary>
        /// Initializes a new instance of the MainWindow class.
        /// </summary>
        public MainWindow()
        {
            // get the kinectSensor object
            this.kinectSensor = KinectSensor.GetDefault();

            // open the reader for the depth frames
            //FrameSourceTypes.Color | 
            this.frameReader = this.kinectSensor.OpenMultiSourceFrameReader(FrameSourceTypes.Color | FrameSourceTypes.Depth);

            // wire handler for frame arrival
            this.frameReader.MultiSourceFrameArrived += this.Reader_FrameArrived;

            // get FrameDescription from DepthFrameSource
            this.depthFrameDescription = this.kinectSensor.DepthFrameSource.FrameDescription;
            this.colorFrameDescription = this.kinectSensor.ColorFrameSource.CreateFrameDescription(ColorImageFormat.Bgra);

            // allocate space to put the pixels being received and converted
            this.depthPixels = new byte[this.depthFrameDescription.Width * this.depthFrameDescription.Height];
            this.depthValues = new ushort[this.depthFrameDescription.Width * this.depthFrameDescription.Height];
            this.colorPixels = new byte[this.colorFrameDescription.Width * this.colorFrameDescription.Height * 4];
            this.processColorArr = new byte[this.colorFrameDescription.Width * this.colorFrameDescription.Height];

            // create the bitmap to display
            this.depthBitmap = new WriteableBitmap(this.depthFrameDescription.Width, this.depthFrameDescription.Height, 96.0, 96.0, PixelFormats.Gray8, null);
            this.colorBitmap = new WriteableBitmap(this.colorFrameDescription.Width, this.colorFrameDescription.Height, 96.0, 96.0, PixelFormats.Bgr32, null);

            // set IsAvailableChanged event notifier
            this.kinectSensor.IsAvailableChanged += this.Sensor_IsAvailableChanged;

            // open the sensor
            this.kinectSensor.Open();

            // use the window object as the view model in this simple example
            this.DataContext = this;

            // initialize the components (controls) of the window
            InitializeComponent();


            // set the status text
            this.statusBarText.Text = this.kinectSensor.IsAvailable ? Properties.Resources.RunningStatusText
                                                            : Properties.Resources.NoSensorStatusText;

            this.Image.Source = this.depthBitmap;
            this.Image2.Source = this.colorBitmap;

            //Check if external directory exists, enable external save location
            if (Directory.Exists("D:\\Kinect Data"))
            {
                extRbtn.IsEnabled = true;
                extAvail = true;
                extRbtn.IsChecked = true;
            }
        }

        /// <summary>
        /// Execute shutdown tasks
        /// </summary>
        /// <param name="sender">object sending the event</param>
        /// <param name="e">event arguments</param>
        private void WindowClosing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (this.frameReader != null)
            {
                // DepthFrameReader is IDisposable
                this.frameReader.Dispose();
                this.frameReader = null;
            }

            if (this.kinectSensor != null)
            {
                this.kinectSensor.Close();
                this.kinectSensor = null;
            }
        }

        /// <summary>
        /// Handles the depth frame data arriving from the sensor
        /// </summary>
        /// <param name="sender">object sending the event</param>
        /// <param name="e">event arguments</param>
        private async void Reader_FrameArrived(object sender, MultiSourceFrameArrivedEventArgs e)
        {
            var reference = e.FrameReference.AcquireFrame();

            bool depthFrameProcessed = false;

            using (DepthFrame depthFrame = reference.DepthFrameReference.AcquireFrame())
            {
                if (depthFrame != null)
                {
                    DepthFrameCounter++;
                    depthFrame.CopyFrameDataToArray(this.depthValues);
                    ProcessDepthFrameData(depthFrame.FrameDescription.LengthInPixels, depthFrame.DepthMinReliableDistance, depthFrame.DepthMaxReliableDistance);
                    depthFrameProcessed = true;
                }
            }

            using (ColorFrame frame = reference.ColorFrameReference.AcquireFrame())
            {
                if (frame != null)
                {
                    ColorFrameCounter++;
                    if (rgbCheckBox.IsChecked.Value == true)
                    {
                        frame.CopyConvertedFrameDataToArray(this.colorPixels, ColorImageFormat.Bgra);
                        processColorArr = ProcessColorArray(this.colorPixels);
                        await Task.Delay(1); //Delay to allow program to buffer
                    }
                    //Draw color frame
                    createKinectColor(frame);
                }
            }

            //Draw depth frame
            if (depthFrameProcessed)
            {
                this.RenderDepthPixels();
            }

            //Write frame to binary file at specified fps
            if (IsRecording && FrameCounter%fps ==0)
            {
                WriteBinFrame();
            }

            //Calculate level
            levelAvg += CalculateLevel();
            //Average frame at fps rate and display every second
            if (FrameCounter % 30 == 0)
            {
                levelAvg /= 30;
                Degree.Text = levelAvg.ToString("F3");

                levelAvg = 0;
            }

            string depthTxt = getFpsText(ref DepthFrameCounter, ref depthTimer, "Depth");
            string colorTxt = getFpsText(ref ColorFrameCounter, ref colorTimer, "RGB");
            if (depthTxt != null)
                depthCheckBox.Content = depthTxt;
            if(colorTxt != null)
                rgbCheckBox.Content = colorTxt;

            
            //Increase frame counter
            FrameCounter++;
        }
        
        private string getFpsText(ref int counter, ref Stopwatch timer, string title)
        {
            const int frameCount = 15;
            string txt = null;
            if (counter % frameCount == 0)
            {
                if (timer.IsRunning)
                {
                    double cfps = frameCount / timer.Elapsed.TotalSeconds;
                    txt = String.Format("{0:s}: {1:f1} fps",title, cfps);
                }
                else
                {
                    timer.Start();
                }
                timer.Restart();
            }
            return txt;
        }

        private Bitmap getColorBitmap()
        {
            Bitmap bmap = null;
            bmap = new Bitmap(1920, 1080, System.Drawing.Imaging.PixelFormat.Format32bppRgb);
            //using (MemoryStream outstream = new MemoryStream())
            //{
            //    BmpBitmapEncoder enc = new BmpBitmapEncoder();
            //    enc.Frames.Add(BitmapFrame.Create((BitmapSource)this.colorBitmap));
            //    enc.Save(outstream);
            //    bmap = new Bitmap(outstream);
            //}
            return bmap;
        }


        /// <summary>
        /// Directly accesses the underlying image buffer of the DepthFrame to 
        /// create a displayable bitmap.
        /// This function requires the /unsafe compiler option as we make use of direct
        /// access to the native memory pointed to by the depthFrameData pointer.
        /// </summary>
        /// <param name="depthFrameData">Pointer to the DepthFrame image data</param>
        /// <param name="depthFrameDataSize">Size of the DepthFrame image data</param>
        /// <param name="minDepth">The minimum reliable depth value for the frame</param>
        /// <param name="maxDepth">The maximum reliable depth value for the frame</param>
        private void ProcessDepthFrameData(uint depthFrameDataSize, ushort minDepth, ushort maxDepth)   
        {
            int depth;

            for (int i = 0; i < depthFrameDataSize; ++i)
            {
                // Get the depth for this pixel
                depth = this.depthValues[i];

                // Normalize depth
                depth = (255 * (depth - minDepth)) / maxDepth;
                if (depth < 0)
                    depth = 0;
                else if (depth > 255)
                    depth = 255;

                // save to bytes
                this.depthPixels[i] = (byte)depth;
            }
        }

        /// <summary>
        /// Renders color pixels into the writeableBitmap.
        /// </summary>
        private void RenderDepthPixels()
        {
            this.depthBitmap.WritePixels(
                new Int32Rect(0, 0, this.depthBitmap.PixelWidth, this.depthBitmap.PixelHeight),
                this.depthPixels,
                this.depthBitmap.PixelWidth,
                0);
        }

        /// <summary>
        /// Handles the user clicking on the screenshot button
        /// </summary>
        /// <param name="sender">object sending the event</param>
        /// <param name="e">event arguments</param>
        private void ButtonScreenshotClick(object sender, RoutedEventArgs e)
        {
            if (kinectSensor == null)
            {
                this.statusBarText.Text = Properties.Resources.ScreenshotFailed;
                return;
            }

            // create a png bitmap encoder which knows how to save a .png file
            BitmapEncoder encoder = new PngBitmapEncoder();

            // create frame from the writable bitmap and add to encoder
            encoder.Frames.Add(BitmapFrame.Create(this.depthBitmap));

            string time = System.DateTime.Now.ToString("hh'-'mm'-'ss", CultureInfo.CurrentUICulture.DateTimeFormat);

            string myPhotos = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);

            string path = Path.Combine(myPhotos, "KinectSnapshot-" + time + ".png");

            // write the new file to disk
            try
            {
                using (FileStream fs = new FileStream(path, FileMode.Create))
                {
                    encoder.Save(fs);
                }

                this.statusBarText.Text = string.Format(CultureInfo.InvariantCulture, "{0} {1}", Properties.Resources.ScreenshotWriteSucceeded, path);
            }
            catch (IOException ex)
            {
                this.statusBarText.Text = string.Format(CultureInfo.InvariantCulture, "{0} {1}", Properties.Resources.ScreenshotWriteFailed, path);
            }
        }

        /// <summary>
        /// Write to binary file
        /// </summary>
        private void WriteBinFrame()
        {
            //Write time stamp as frame header
            DateTime time_stamp = System.DateTime.Now;
            //string time_stamp = System.DateTime.Now.ToString("hh:mm:ss.fff", CultureInfo.CurrentUICulture.DateTimeFormat);
            writer.Write((short)time_stamp.Hour);
            writer.Write((short)time_stamp.Minute);
            writer.Write((short)time_stamp.Second);
            writer.Write((short)time_stamp.Millisecond);

            //If depth file box checked
            if (depthCheckBox.IsChecked.Value == true)
            {
                for (int i = 0; i < this.depthPixels.Length; ++i)
                {
                    //Write depth for this pixel as ushort to file
                    ushort depth = depthValues[i];
                    writer.Write(depth);
                }
            }


            //If color file box checked
            if (rgbCheckBox.IsChecked.Value == true)
            {
                Bitmap bmp = getColorBitmap();
                //colorWriter.WriteVideoFrame(bmp);
                ////Write color array as byte array to file
                //writer.Write(processColorArr);
            }
        }

        private void Record_Click(object sender, RoutedEventArgs e)
        {
            //If recording
            if (!IsRecording)
            {
                ////Check sampling rate and capture mode compatibility
                ////If sampling is between 0 and 12 Hz, allow both
                //if (Int32.Parse(SamplingFreqTbox.Text) <= 12 && Int32.Parse(SamplingFreqTbox.Text) > 0)
                //{
                //    depthCheckBox.IsEnabled = true;
                //    depthCheckBox.IsChecked = true;
                //    rgbCheckBox.IsEnabled = true;
                //    rgbCheckBox.IsChecked = true;
                //}
                ////If between 12 and 30 Hz, allow only depth
                //else 
                if (Int32.Parse(SamplingFreqTbox.Text) <= 30 && Int32.Parse(SamplingFreqTbox.Text) >0)
                {
                    depthCheckBox.IsEnabled = true;
                    //depthCheckBox.IsChecked = true;
                    rgbCheckBox.IsEnabled = true;
                    //rgbCheckBox.IsChecked = true;
                }
                //If 0 or negative or greater than 30, exception; return null 
                else
                {
                    System.Windows.MessageBox.Show("Check that the sampling frequency is between 1 and 30 Hz");
                    return;
                }

                //Determine fps from text box for file append
                fps = 30 / Int32.Parse(SamplingFreqTbox.Text);

                //Set Directory Path
                //string myPath = Path.Combine("D:\\Kinect Data");
                //string myPath = Path.Combine("C:\\Airway_Resistance_2015\\Airway_Data_2015\\Kinect Data");
                //string myPath = Path.Combine("C:\\Users\\AC lab\\Desktop\\Kinect Apps\\RecorderFinal\\Sample Kinect Data");
                string myPath = Path.Combine(Directory.GetDirectories(Directory.GetParent(Directory.GetParent(Directory.GetCurrentDirectory()).ToString()).ToString(), "Sample*")[0]);

                //If internal save location checked, save in project folder
                if (intRbtn.IsChecked == true)
                {
                    myPath = Path.Combine(Directory.GetDirectories(Directory.GetParent(Directory.GetParent(Directory.GetCurrentDirectory()).ToString()).ToString(), "Kinect*")[0]);
                }

                //If external save location checked, save on hard drive
                if (extRbtn.IsChecked == true)
                {
                    myPath = Path.Combine("D:\\Kinect Data");
                }

                //Format file name
                //Time
                string time = System.DateTime.Now.ToString("yyyy'-'M'-'d'-'hh'-'mm'-'ss", CultureInfo.CurrentUICulture.DateTimeFormat);

                string fname = null;
                string fname2 = null;
                //Append depth and color to file name, if depth file checked
                if (depthCheckBox.IsChecked.Value == true && rgbCheckBox.IsChecked.Value == true)
                {
                    fname = String.Format("Kinect_{0}_{1}_{2}Hz_depth.bin", Sub.Text, time, SamplingFreqTbox.Text);
                    fname2 = String.Format("Kinect_{0}_{1}_{2}Hz_RGB.avi", Sub.Text, time, SamplingFreqTbox.Text);
                    string path_avi = Path.Combine(myPath, fname2);
                    colorWriter = new VideoFileWriter();
                    colorWriter.Open(path_avi, this.colorFrameDescription.Width, this.colorFrameDescription.Height, 30, VideoCodec.MPEG4);

                }
                else
                {
                    //Append depth to file name, if depth file checked
                    if (depthCheckBox.IsChecked.Value == true)
                    {
                        fname = String.Format("Kinect_{0}_{1}_{2}Hz_depth.bin", Sub.Text, time, SamplingFreqTbox.Text);
                    }
                    //Append color to file name, if color file checked
                    if (rgbCheckBox.IsChecked.Value == true)
                    {
                        fname = String.Format("Kinect_{0}_{1}_{2}Hz_rgb.bin", Sub.Text, time, SamplingFreqTbox.Text);
                    }
                }

                //Disable choose file type when start recording
                depthCheckBox.IsEnabled = false;
                rgbCheckBox.IsEnabled = false;

                //Disable sampling frequency and file name when recording
                SamplingFreqTbox.IsEnabled = false;
                Sub.IsEnabled = false;

                

                //Create file path, file, and writer
                string path_bin= Path.Combine(myPath, fname);
                FileStream SourceStream = File.Open(path_bin, FileMode.OpenOrCreate, FileAccess.Write);
                writer = new BinaryWriter(SourceStream);

                //Disable save location radio buttons
                intRbtn.IsEnabled = false;
                extRbtn.IsEnabled = false;

                //Toggle recording
                IsRecording = !IsRecording;

                //Change button text
                Record.Content = IsRecording ? "Stop" : "Record";
            }
            else
            {
                //Close writer and enable file type boxes
                writer.Close();
                if(colorWriter != null)
                    colorWriter.Close();
                depthCheckBox.IsEnabled = true;
                rgbCheckBox.IsEnabled = true;

                //Re-enable sampling frequency and file name boxes
                Sub.IsEnabled = true;
                SamplingFreqTbox.IsEnabled = true;

                //Enable radio button save locations
                intRbtn.IsEnabled = true;
                if (extAvail == true)
                {
                    extRbtn.IsEnabled = true;
                }

                //Toggle recording
                IsRecording = !IsRecording;

                //Change button text
                Record.Content = IsRecording ? "Stop" : "Record";
            }
        }

        /// <summary>
        /// Calculate if camera is level to background surface
        /// </summary>
        private double CalculateLevel()
        {
            if (kinectSensor == null)
                return 0;

            //Declare variables
            double level, lever, val;
            int width, height, x, y, idx;
            int xmar, ymar;
            width = kinectSensor.DepthFrameSource.FrameDescription.Width;
            height = kinectSensor.DepthFrameSource.FrameDescription.Height;

            //Set x and y margins as area of interest to level
            xmar = 200;
            ymar = 150;

            level = 0;
            idx = 0;
            for (y = 0; y < height; y++)
            {
                //Within y margins
                if (y < ymar || y > (height - ymar))
                    continue;

                for (x = 0; x < width; x++)
                {
                    //Within y margins

                    if (x < xmar || x > (width - xmar))
                        continue;

                    //Determine index and copy depth
                    idx = x + y * width;
                    val = this.depthValues[idx];
                    
                    //Weighted average values to determine level of Kinect
                    lever = y - height / 2;
                    level += lever * val;

                    idx++;
                }
            }
            
            //Average level over frame
            level = level / width / height;

            return level;
        }

        /// <summary>
        /// Handles the event which the sensor becomes unavailable (E.g. paused, closed, unplugged).
        /// </summary>
        /// <param name="sender">object sending the event</param>
        /// <param name="e">event arguments</param>
        private void Sensor_IsAvailableChanged(object sender, IsAvailableChangedEventArgs e)
        {
            // on failure, set the status text
            this.statusBarText.Text = this.kinectSensor.IsAvailable ? Properties.Resources.RunningStatusText
                                                            : Properties.Resources.SensorNotAvailableStatusText;
        }

        delegate void createKinectColorDelegate(ColorFrame frame);

        /// <summary>
        /// Convert frame data to picture to draw
        /// </summary>
        private void createKinectColor(ColorFrame frame)
        {
            using (KinectBuffer colorBuffer = frame.LockRawImageBuffer())
            {
                colorBitmap.Lock();

                // verify data and write the new color frame data to the display bitmap
                if ((colorFrameDescription.Width == colorBitmap.PixelWidth) && (colorFrameDescription.Height == colorBitmap.PixelHeight))
                {
                    frame.CopyConvertedFrameDataToIntPtr(
                        colorBitmap.BackBuffer,
                        (uint)(colorFrameDescription.Width * colorFrameDescription.Height * 4),
                        ColorImageFormat.Bgra);

                    //Draw color frame
                    colorBitmap.AddDirtyRect(new Int32Rect(0, 0, colorBitmap.PixelWidth, colorBitmap.PixelHeight));
                }

                colorBitmap.Unlock();
            }
        }

        /// <summary>
        /// Take only one color from color frame to write to binary
        /// </summary>
        private byte[] ProcessColorArray(byte[] arrIn)
        {
            byte[] arrOut = new byte[this.colorFrameDescription.Width * this.colorFrameDescription.Height];
            int count = 0;
            for (int i = 0; i < colorPixels.Length; i++)
            {
                //Choose color intensity to write to file
                //1 = Nothing
                //2 = RED
                //3 = GREEN
                //0 = BLUE
                if ((i + 0) % 4 == 0)
                {
                    arrOut[count] = arrIn[i];
                    count++;
                }
            }

            return arrOut;
        }
    }
}