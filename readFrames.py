from __future__ import division

import numpy as np
import gzip

import os
import matplotlib.pyplot as plt

def loadFrames(fname):
    w,h,headerSize = 512,424,4
    with gzip.open(fname, mode='rb') as f:
        # read data
        raw = f.read()
        data = np.frombuffer(raw, dtype=np.int16)
        raw = []

        # extract frames and header
        fLen = w*h + headerSize
        data = data.reshape((-1, fLen))
        header = data[:,:headerSize]
        frames = data[:,headerSize:]
        data = []

        # convert frames
        frames = np.cumsum(frames,axis=0) # reverse the delta compression
        frames = frames.reshape((-1,h,w)).astype(float) # convert to floating point

        # convert header: time_s is times in seconds
        time_s = header.astype(float).dot([60*60, 60, 1, 1e-3])

        return time_s, frames


# Demonstration: feel free to delete this
fname = 'Kinect_FileName_2017-2-19-03-15-52_depth.bin.gz'
time_s, frames = loadFrames('Kinect Data/' + fname)

fn = 10
plt.figure('frame {}'.format(fn))
plt.imshow(frames[fn]) # show frame 10

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
