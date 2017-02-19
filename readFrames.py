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
        frames = np.cumsum(frames,axis=0)
        data = []

        # convert frames
        frames = frames.reshape((-1,h,w)).astype(float)

        # convert header
        plt.imshow(frames[10])
        plt.show()


fname = 'input.bin.gz'
fname = 'Kinect_FileName_2017-2-19-01-06-59_depth.bin.gz'
fname = 'Kinect_FileName_2017-2-19-01-09-06_depth.bin.gz'
fname = 'Kinect_FileName_2017-2-19-03-15-52_depth.bin.gz'
loadFrames('Kinect Data/' + fname)
