//------------------------------------------------------------------------------
// <copyright file="MainWindow.xaml.cs" company="Microsoft">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

namespace Northeastern.aclab.Kinect
{
    using System;
    using System.Globalization;
    using System.IO;
    using System.IO.Compression;
    using System.Windows;
    using System.Windows.Media;
    using System.Windows.Media.Imaging;
    using Microsoft.Kinect;
    using System.Threading.Tasks;
    using System.Diagnostics;
    using AForge.Video.FFMPEG;
    using System.Drawing;
    using System.Linq;

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
        private Bitmap colorBM = null;

        /// <summary>
        /// Intermediate storage for frame data
        /// </summary>
        private byte[] depthPixels = null;
        private ushort[] depthValues = null;
        private short[] lastFrame = null;
        private byte[] depthValuesBuf = null;
        private byte[] colorPixels = null;
        private byte[] processColorArr = null;

        //Initialize some global variables
        private double frameCounter;
        private Stopwatch timer;
        private Stopwatch recordTimer = new Stopwatch();
        private TimeSpan frameTime;
        private TimeSpan procTime;
        private const double GM_PCT = 0.96;
        private const double PROC_CUTOFF = 0.95;
        private int simpleFrameCounter = 0;

        private bool IsRecording = false;
        private GZipStream gzWriter;
        private bool extAvail = false;
        private double levelAvg = 0;

        private VideoFileWriter colorWriter = null;
        private BinaryWriter timeWriter = null;

        public void initTimers()
        {
            timer = new Stopwatch();
            frameCounter = 0;
            frameTime = new TimeSpan(0, 0, 0);
            procTime = new TimeSpan(0, 0, 0);
            timer.Start();
        }

        // Call at top of frame
        public void updateTimer(ref TimeSpan ts)
        {
            double tmNew = timer.Elapsed.TotalSeconds;
            double tmOld = ts.TotalSeconds;
            //ts = TimeSpan.FromSeconds(tmOld + tmNew);
            ts = TimeSpan.FromSeconds(tmOld * GM_PCT + tmNew /* * (1 - GM_PCT) */);

        }
        public void addTotalTime()
        {
            updateTimer(ref frameTime);           
            timer.Restart();
        }
        // call at end of frame
        public void addProcTime()
        {
            updateTimer(ref procTime);
        }
        public void addFrames(int frames)
        {
            frameCounter = GM_PCT * frameCounter + /*(1 - GM_PCT) * */frames;
            //frameCounter++;
        }
        public double getFPS()
        {
            double tm = frameTime.TotalSeconds;
            if (tm < 1e-5)
                return 0;
            else
                return frameCounter / tm;
        }
        public bool shouldProcFrame()
        {
            double totalTime = frameTime.TotalSeconds;
            double pTime = procTime.TotalSeconds;
            double ratio = 0;
            if (totalTime > 1e-5)
                ratio = pTime / totalTime;
            //System.Console.WriteLine(string.Format("{0}, {1}: {2:0.00}", procTime, totalTime, ratio));
            return ratio < PROC_CUTOFF;
        }
       
        /// <summary>
        /// Initializes a new instance of the MainWindow class.
        /// </summary>
        public MainWindow()
        {
            initTimers();

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

            Sub.Text = (string)Properties.Settings.Default["Filename"];
            SaveDirectory.Text = (string)Properties.Settings.Default["SaveDirectory"];

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
            addTotalTime();

            var reference = e.FrameReference.AcquireFrame();
            bool gotDepthFrame = false, gotRGBFrame = false;
            bool shouldSave = shouldProcFrame();


            using (DepthFrame depthFrame = reference.DepthFrameReference.AcquireFrame())
            {
                if (depthFrame != null && shouldSave)
                {
                    gotDepthFrame = true;
                    depthFrame.CopyFrameDataToArray(this.depthValues);
                    ProcessDepthFrameData(depthFrame.FrameDescription.LengthInPixels, depthFrame.DepthMinReliableDistance, depthFrame.DepthMaxReliableDistance);
                }
            }

            using (ColorFrame frame = reference.ColorFrameReference.AcquireFrame())
            {
                if (frame != null && shouldSave)
                {
                    gotRGBFrame = true;
                    if (rgbCheckBox.IsChecked.Value == true)
                        makeColorBitmap(frame);
                    ////Draw color frame
                    createKinectColor(frame);
                }
            }

            //Draw depth frame
            if (gotDepthFrame)
            {
                this.RenderDepthPixels();
            }

            //Write frame to binary file at specified fps
            if (shouldSave && gotDepthFrame && gotRGBFrame)
            {
                addFrames(1);
                if(IsRecording)
                    WriteBinFrame();
            } else
            {
                addFrames(0);
            }

            if(gotDepthFrame && gotRGBFrame)
            {

                depthCheckBox.Content = (bool)depthCheckBox.IsChecked ? string.Format("Depth: {0:0.0} fps", getFPS()) : "Depth";
                rgbCheckBox.Content = (bool)rgbCheckBox.IsChecked ? string.Format("RGB: {0:0.0 fps}", getFPS()) : "RGB";

                simpleFrameCounter++;
                //Calculate level
                levelAvg += CalculateLevel();
                //Average frame at fps rate and display every second
                if (simpleFrameCounter % 30 == 0)
                {
                    levelAvg /= 30;
                    Degree.Text = levelAvg.ToString("F3");


                    levelAvg = 0;
                }
            }

            addProcTime(); 
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

        private void makeColorBitmap(ColorFrame frame)
        {
            int w = frame.FrameDescription.Width;
            int h = frame.FrameDescription.Height;
            System.Drawing.Imaging.PixelFormat fmt = System.Drawing.Imaging.PixelFormat.Format32bppRgb;
            System.Drawing.Imaging.BitmapData bmd;

            if (colorBM == null)
            {
                colorBM = new Bitmap(w, h, fmt);
            }
            bmd = colorBM.LockBits(new Rectangle(0, 0, w, h), System.Drawing.Imaging.ImageLockMode.WriteOnly, fmt);
            frame.CopyConvertedFrameDataToIntPtr(bmd.Scan0, (uint)(w * h * 4), ColorImageFormat.Bgra);
            colorBM.UnlockBits(bmd);
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

        private void WriteShort(short val)
        {
            byte[] v = new byte[2];
            v[0] = (byte)(val & 0xFF);
            v[1] = (byte)((val >> 8) & 0xFF);

            timeWriter.Write(v, 0, 2);
        }

        /// <summary>
        /// Write to binary file
        /// </summary>
        private void WriteBinFrame()
        {
            //Write time stamp as frame header
            DateTime time_stamp = System.DateTime.Now;
            //string time_stamp = System.DateTime.Now.ToString("hh:mm:ss.fff", CultureInfo.CurrentUICulture.DateTimeFormat);
            WriteShort((short)time_stamp.Hour);
            WriteShort((short)time_stamp.Minute);
            WriteShort((short)time_stamp.Second);
            WriteShort((short)time_stamp.Millisecond);

            // Write frame
            if (depthCheckBox.IsChecked.Value)
            {
                if (lastFrame == null || lastFrame.Length != depthValues.Length)
                    lastFrame = Enumerable.Repeat<short>(0, depthValues.Length).ToArray();
                int len = depthValues.Length * 2;
                if (depthValuesBuf == null || depthValuesBuf.Length != len)
                    depthValuesBuf = new byte[len];
                for (int idx = 0; idx < depthValues.Length; idx++)
                {
                    // Delta compression
                    short val = (short)((short)depthValues[idx] - lastFrame[idx]);
                    lastFrame[idx] = (short)depthValues[idx];

                    // Convert short array to bytes
                    depthValuesBuf[idx * 2] = (byte)(val & 0xFF);
                    depthValuesBuf[idx * 2 + 1] = (byte)((val >> 8) & 0xFF);
                }
                // For the GZStream (the compression stream), it is MUCH, MUCH more efficient to
                // write many bytes at once than to write a byte at a time. When I was writing a short
                // at a time, then we could only get 5 fps
                gzWriter.Write(depthValuesBuf, 0, len);
            }
            gzWriter.Flush();


            //If color file box checked
            if (rgbCheckBox.IsChecked.Value == true)
            {
                colorWriter.WriteVideoFrame(colorBM,recordTimer.Elapsed);
            }
        }

        private void Record_Click(object sender, RoutedEventArgs e)
        {
            //If recording
            if (!IsRecording)
            {


                //Set Directory Path
                //string myPath = Path.Combine("D:\\Kinect Data");
                //string myPath = Path.Combine("C:\\Airway_Resistance_2015\\Airway_Data_2015\\Kinect Data");
                //string myPath = Path.Combine("C:\\Users\\AC lab\\Desktop\\Kinect Apps\\RecorderFinal\\Sample Kinect Data");
                string myPath = SaveDirectory.Text;

                // Make sure directory exists
                if(! Directory.Exists(myPath))
                {
                    MessageBox.Show("Recording directory doesn't exist");
                    return;
                }

                // Create directory for files
                string time = System.DateTime.Now.ToString("yyyy'-'MM'-'dd'-'hh'-'mm'-'ss", CultureInfo.CurrentUICulture.DateTimeFormat);
                myPath = Path.Combine(myPath,String.Format("Kinect_{0}_{1}", Sub.Text, time));
                Directory.CreateDirectory(myPath);

                Properties.Settings.Default["Filename"] = Sub.Text;
                Properties.Settings.Default.Save();

                initTimers();

                //Format file name
                //Time

                gzWriter = null;
                colorWriter = null;
                timeWriter = null;

                // Create Time Writer
                string tfname = Path.Combine(myPath, "times.bin");
                FileStream tfs = File.Open(tfname, FileMode.OpenOrCreate, FileAccess.Write);
                timeWriter = new BinaryWriter(tfs);

                //Append depth and color to file name, if depth file checked
                lastFrame = null;
                if (depthCheckBox.IsChecked.Value == true)
                {
                    string fname = Path.Combine(myPath, "depth.bin.gz");
                    FileStream fileStream = File.Open(fname, FileMode.OpenOrCreate, FileAccess.Write);
                    gzWriter = new GZipStream(fileStream, CompressionLevel.Fastest);
                }

                if (rgbCheckBox.IsChecked.Value == true)
                {
                    string fname = Path.Combine(myPath, "rgb.avi");
                    colorWriter = new VideoFileWriter();
                    colorWriter.Open(fname, this.colorFrameDescription.Width, this.colorFrameDescription.Height, 30, VideoCodec.MPEG4);
                    recordTimer.Restart();
                }
                //Create file path, file, and writer

                //Disable choose file type when start recording
                depthCheckBox.IsEnabled = false;
                rgbCheckBox.IsEnabled = false;

                //Disable sampling frequency and file name when recording
                Sub.IsEnabled = false;
                ChangeDirectory.IsEnabled = false;


                //Toggle recording
                IsRecording = !IsRecording;

                //Change button text
                Record.Content = IsRecording ? "Stop" : "Record";
            }
            else
            {
                //Close writer and enable file type boxes
                if(timeWriter != null)
                    timeWriter.Close();
                if(gzWriter != null)
                    gzWriter.Close();
                if(colorWriter != null)
                    colorWriter.Close();
                timeWriter = null;
                gzWriter = null;
                colorWriter = null;

                depthCheckBox.IsEnabled = true;
                rgbCheckBox.IsEnabled = true;

                //Re-enable sampling frequency and file name boxes
                Sub.IsEnabled = true;
                ChangeDirectory.IsEnabled = true;


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

        private void ChangeDirectory_Click(object sender, RoutedEventArgs e)
        {
            using (var fbd = new System.Windows.Forms.FolderBrowserDialog())
            {
                string sdir = (string)Properties.Settings.Default["SaveDirectory"];
                if(Directory.Exists(sdir))
                {
                    fbd.SelectedPath = sdir;
                }
                var result = fbd.ShowDialog();
                if(result == System.Windows.Forms.DialogResult.OK)
                {
                    sdir = fbd.SelectedPath;
                    SaveDirectory.Text = sdir;
                    Properties.Settings.Default["SaveDirectory"] = sdir;
                    Properties.Settings.Default.Save();
                }
            }
            
        }
    }
}