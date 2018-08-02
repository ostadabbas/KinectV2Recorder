# This uses python version 2.7+

from __future__ import division

import gzip
import scipy.misc

import os
import matplotlib.pyplot as plt
import numpy as np

import imageio

def loadFrames(fname, startFrame=0, nFrames=200):
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

        frames = frames[startFrame:nFrames,:]

        # convert frames
        frames = np.cumsum(frames,axis=0) # reverse the delta compression
        frames = frames.reshape((-1,h,w)).astype(float) # convert to floating point

    vfname = os.path.join(fname, 'rgb.avi')
    vid = None
    if os.path.exists(vfname):
        vid = imageio.get_reader(vfname, 'ffmpeg')


    return time_s, frames, vid


# Demonstration: feel free to delete this
# fname = 'Kinect_FileName_2017-2-19-03-15-52_depth.bin.gz'
#fname = 'Kinect_FileName_2017-4-9-06-48-02'
fname2 = 'C:\Users\AC-Lab\Documents\RecordedPC\Kinect_30sec_2018-07-26-04-24-24'
outdir = 'C:\Users\AC-Lab\Documents\RecordedPC\Kinect_30sec_2018-07-26-04-24-24'
#time_s, frames, vid = loadFrames('Kinect Data/' + fname)

startFrame=0
nFrames=55
fn = 10
saveFrames=[1,20,40,50]


time_s, frames, vid = loadFrames(fname2, startFrame, nFrames)

for n in saveFrames:
    print(n)
    bname = os.path.join(outdir, 'out{0:02d}'.format(n))
    np.savetxt('{}.csv'.format(bname), frames[n], delimiter=',')
    scipy.misc.imsave('{}.jpg'.format(bname), vid.get_data(n))

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
