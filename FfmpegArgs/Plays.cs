using System;
using System.Collections.Generic;
using System.Text;

namespace FfmpegArgs
{
    class Plays
    {
        public string league { get; set; }
        public string season { get; set; }
        public string seasonType { get; set; }
        public string week { get; set; }
        public string videoSource { get; set; }
        public string gamekey { get; set; }
        public string playID { get; set; }
        public string markInFrame { get; set; }
        public string markoutFrame { get; set; }
        public string cameraView { get; set; }

        public double startTime
        {
            get
            {
                return frameToTime(System.Convert.ToInt32(markInFrame));
            }
        }

        public double duration
        {
            get
            {
                var r = System.Convert.ToInt32(markoutFrame) - System.Convert.ToInt32(markInFrame);

                return frameToTime(r);
            }
        }

        public string outputNameforBlob
        {
            get
            {
                return season + "/" + seasonType + "/" + week + "/" + gamekey + "_" + System.Convert.ToInt32(playID).ToString("000000") + "_" + cameraView + ".mp4";
            }
        }

        public string outputName
        {
            get
            {
                return gamekey + "_" + System.Convert.ToInt32(playID).ToString("000000") + "_" + cameraView + ".mp4";
            }
        }

        public double frameToTime(int frames)
        {
            var frameRate = 59.94;
            //var frames = System.Convert.ToInt32(txtDuration.Text);
            return (frames / frameRate);
        }
    }
}
