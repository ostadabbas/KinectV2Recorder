Kinect video playback and record (this is the recorder)

- To compile: you need to get and install the AForge libraries http://www.aforgenet.com/
  - make sure to install AForge Video and AForge Video FFMPEG

- Playback is with the playback app.
- also look at the readFrames.py python script in this directory to see how
  to load the depth data from python. The video is standard AVI

This records to three files:
  - times.bin: the time stamps for each frame
  - rgb.avi: an AVI with the video data
  - depth.bin.gz: A compressed set of frames. The frames are delta encoded and stored as 16 bit values (little endian). Once again, the python or c# playback code should give info on the recording format
