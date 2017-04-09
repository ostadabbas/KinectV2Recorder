# This uses python version 2.7+

from __future__ import division

import numpy as np
import gzip

import os
import matplotlib.pyplot as plt

import imageio

def loadFrames(fname):
    w,h,headerSize = 512,424,4

    time_s = None
    frames = None
    vid = None
    with open(os.path.join(fname,'times.bin'), mode='rb') as f:
        raw = f.read()
        data = np.frombuffer(raw, dtype=np.int16)
        raw = []

        header = data.reshape((-1, headerSize))
        time_s = header.astype(float).dot([60*60, 60, 1, 1e-3])
        data = []

    with gzip.open(os.path.join(fname, 'depth.bin.gz'), mode='rb') as f:
        # read data
        raw = f.read()
        data = np.frombuffer(raw, dtype=np.int16)
        raw = []

        # extract frames and header
        fLen = w*h 
        frames = data.reshape((-1, fLen))
        data = []

        # convert frames
        frames = np.cumsum(frames,axis=0) # reverse the delta compression
        frames = frames.reshape((-1,h,w)).astype(float) # convert to floating point

    vfname = os.path.join(fname, 'rgb.avi')
    if os.path.exists(vfname):
        vid = imageio.get_reader(vfname, 'ffmpeg')


    return time_s, frames, vid


# Demonstration: feel free to delete this
# fname = 'Kinect_FileName_2017-2-19-03-15-52_depth.bin.gz'
fname = 'Kinect_FileName_2017-4-9-06-48-02'
time_s, frames, vid = loadFrames('Kinect Data/' + fname)

fn = 10
plt.figure('frame {}'.format(fn))
plt.imshow(frames[fn]) # show frame 10

plt.figure('rgb {}'.format(fn))
plt.imshow(vid.get_data(fn))

plt.figure('times')
time2 = time_s - time_s[0] # time in seconds from start
plt.title('Times')
plt.xlabel('frame number')
plt.ylabel('time (s)')
plt.plot(time_s)

fps = 1/(time_s[1:] - time_s[:-1])
plt.figure('fps vs time')
plt.plot(time2[1:],fps)
plt.xlabel('time (s)')
plt.ylabel('fps')

plt.show()
