using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OpenCvSharp;

namespace AI_COMPNENT_SAFTY.Services
{
    class CameraService
    {
        private VideoCapture capture;

        public void Start(int cameraIndex = 0)
        {
            capture = new VideoCapture(cameraIndex);

            if (!capture.IsOpened())
                throw new Exception("Camera not opened");
                
        }

        public Mat GetFrame()
        {
            if (capture == null || !capture.IsOpened())
                return null;

            var frame = new Mat();
            capture.Read(frame);

            return frame;
        }

        public void Stop()
        {
            capture?.Release();
        }
    }
}
