using System.Collections.Generic;
using UnityEngine;
using TMPro;


namespace BuildingVolumes.Player
{
    public class GSFrameDebugger : MonoBehaviour
    {
        [SerializeField]
        int framerateLimit;
        int lastFramerateLimit;

        BufferedGeometryReader reader;

        [SerializeField]
        Canvas canvas;

        [SerializeField]
        TextMeshProUGUI fps_TMP, frameTimeSmoothed_TMP, frameTime_TMP, targetFrameTime_TMP, applicationFPS_TMP, applicationDeltaTime_TMP, frameDropped_TMP;

        [SerializeField]
        TextMeshProUGUI bufferSize_TMP, bufferIndex_TMP, playbackIndex_TMP;

        [SerializeField]
        TextMeshProUGUI preBufferedFrames_TMP, totalBufferedFrames_TMP, framesReading_TMP, framesLoading_TMP;

        [SerializeField]
        GameObject frameVizGrid;

        [SerializeField]
        GameObject frameVizPrefab;

        List<GSFrameViz> framesViz = new List<GSFrameViz>();

        float smoothedApplicationFPS;
        float smoothedFrameTiming;

        private void Update()
        {
            if (lastFramerateLimit != framerateLimit)
            {
                QualitySettings.vSyncCount = 0;

                if (framerateLimit > 0)
                    Application.targetFrameRate = framerateLimit;
                else
                    Application.targetFrameRate = -1;

                lastFramerateLimit = framerateLimit;
            }

            float decay = 0.5f;
            smoothedApplicationFPS = decay * smoothedApplicationFPS + (1.0f - decay) * (1.0f / Time.deltaTime);
            int applicationDeltaTime = Mathf.RoundToInt(Time.deltaTime * 1000);

            applicationFPS_TMP.text = "FPS: " + Mathf.RoundToInt(smoothedApplicationFPS);
            applicationDeltaTime_TMP.text = "Deltatime " + applicationDeltaTime;
            applicationFPS_TMP.color = PerfRatingHitMinimum(smoothedApplicationFPS, 30, 0.9f, 0.8f);
            applicationDeltaTime_TMP.color = PerfRatingExceedMinimum(applicationDeltaTime, 34, 1.2f);
        }

        // Update is called once per frame
        public void UpdateFrameDebugger(GeometrySequenceStream sourceStream)
        {
            reader = sourceStream.bufferedReader;

            if (reader != null)
            {
                int bufferTotalSize = reader.bufferSize;

                if (framesViz.Count != bufferTotalSize)
                {
                    foreach (GSFrameViz viz in framesViz)
                    {
                        Destroy(viz.gameObject);
                    }

                    framesViz.Clear();

                    for (int i = 0; i < bufferTotalSize; i++)
                    {
                        GameObject go = Instantiate(frameVizPrefab);
                        go.transform.SetParent(frameVizGrid.transform, false);
                        GSFrameViz viz = go.GetComponent<GSFrameViz>();
                        framesViz.Add(viz);
                    }
                }

                int fps = Mathf.RoundToInt(sourceStream.smoothedFPS);
                int frameTime = Mathf.RoundToInt(sourceStream.lastFrameTime);
                int targetFrameTime = Mathf.RoundToInt(sourceStream.targetFrameTimeMs);

                float decay = 0.95f;
                smoothedFrameTiming = decay * smoothedFrameTiming + (1.0f - decay) * frameTime;

                int playbackFrame = sourceStream.lastFrameIndex;
                int bufferIndex = sourceStream.lastFrameBufferIndex;

                Frame[] buffer = reader.frameBuffer;

                int totalBufferedFrames = 0;
                int preBufferedFrames = 0;
                int framesReading = 0;
                int framesLoading = 0;

                for (int i = 0; i < buffer.Length; i++)
                {
                    framesViz[i].SetBufferState(buffer[i].bufferState);
                    framesViz[i].SetPlaybackFrameNumber(buffer[i].playbackIndex);
                    framesViz[i].SetBufferFrameNumber(i);

                    switch (buffer[i].bufferState)
                    {
                        case BufferState.Empty:
                            break;
                        case BufferState.Consumed:
                            break;
                        case BufferState.Reading:
                            framesReading++;
                            break;
                        case BufferState.Loading:
                            framesLoading++;
                            break;
                        case BufferState.Ready:
                            totalBufferedFrames++;
                            break;

                    }
                }

                if (sourceStream.lastFrameBufferIndex > -1)
                {
                    framesViz[sourceStream.lastFrameBufferIndex].SetAsShown();
                }

                bool bufferedFrameInSuccession = true;
                int successionFrame = playbackFrame;

                while (bufferedFrameInSuccession)
                {
                    successionFrame++;

                    if (successionFrame >= reader.totalFrames)
                    {
                        successionFrame = 0;
                    }

                    bufferedFrameInSuccession = false;

                    foreach (Frame frame in buffer)
                    {
                        if (frame.playbackIndex == successionFrame && frame.bufferState == BufferState.Ready)
                        {
                            bufferedFrameInSuccession = true;
                            preBufferedFrames++;
                        }
                    }
                }

                fps_TMP.text = "FPS: " + fps;
                frameTimeSmoothed_TMP.text = "Frametime (smoothed): " + Mathf.RoundToInt(smoothedFrameTiming);
                frameTime_TMP.text = "Frametime (actual): " + frameTime;
                targetFrameTime_TMP.text = "Target frametime: " + targetFrameTime;
                frameDropped_TMP.text = "Frames dropped: " + sourceStream.framesDroppedCounter;
                bufferSize_TMP.text = "Buffer Size: " + bufferTotalSize;
                bufferIndex_TMP.text = "Buffer Index: " + bufferIndex;
                playbackIndex_TMP.text = "Playback Index: " + playbackFrame;
                totalBufferedFrames_TMP.text = "Frames Buffered: " + totalBufferedFrames;
                preBufferedFrames_TMP.text = "Pre-Buffered Frames: " + preBufferedFrames;
                framesReading_TMP.text = "Frames Reading (Job): " + framesReading;
                framesLoading_TMP.text = "Frames Loading (Main Thread): " + framesLoading;

                //Rate the buffer performance based on the percentage of readily available frames
                fps_TMP.color = PerfRatingReachMaximum(fps, 1000 / targetFrameTime, 0.9f, 0.8f, false);
                frameTimeSmoothed_TMP.color = PerfRatingDifference(smoothedFrameTiming, targetFrameTime, 0.1f, 0.2f);
                frameTime_TMP.color = PerfRatingDifference(frameTime, targetFrameTime, 0.1f, 0.2f);
                frameDropped_TMP.color = sourceStream.frameDropped ? Color.red : Color.white;
                totalBufferedFrames_TMP.color = PerfRatingReachMaximum(totalBufferedFrames, bufferTotalSize, 0.5f, 0.8f, false);
                preBufferedFrames_TMP.color = PerfRatingReachMaximum(preBufferedFrames, totalBufferedFrames, 0.3f, 0.5f, false);
                framesReading_TMP.color = PerfRatingReachMaximum(framesReading, totalBufferedFrames, 0.9f, 0.7f, true);
                framesLoading_TMP.color = PerfRatingReachMaximum(framesLoading, totalBufferedFrames, 0.9f, 0.7f, true);
            }
        }

        Color PerfRatingReachMaximum(float isValue, float maxValue, float goodPercentValue, float mediumPercentValue, bool invert)
        {
            float percentage = (1f / maxValue) * isValue;
            if (percentage > 1f)
                percentage = 1f;
            if (invert)
                percentage = 1f - percentage;

            if (percentage >= goodPercentValue)
                return Color.green;
            else if (percentage <= goodPercentValue && percentage >= mediumPercentValue)
                return Color.yellow;
            else if (percentage <= mediumPercentValue)
                return Color.red;
            else return Color.white;
        }

        Color PerfRatingHitMinimum(float isValue, float minValue, float mediumPercentValue, float badPercentValue)
        {
            float percentage = (1f / minValue) * isValue;
            if (percentage > 1f)
                percentage = 1f;

            if (percentage >= mediumPercentValue)
                return Color.green;
            else if (percentage <= mediumPercentValue && percentage >= badPercentValue)
                return Color.yellow;
            else if (percentage <= badPercentValue)
                return Color.red;
            else return Color.white;
        }

        Color PerfRatingExceedMinimum(float isValue, float minValue, float mediumExceedPercentage)
        {
            float percentage = (1f / minValue) * isValue;

            if (percentage <= 1)
                return Color.green;
            else if (percentage > 1 && percentage <= mediumExceedPercentage)
                return Color.yellow;
            else if (percentage >= mediumExceedPercentage)
                return Color.red;
            else return Color.white;
        }

        Color PerfRatingDifference(float isValue, float shouldValue, float goodPercentDifference, float mediumPercentDifference)
        {
            float difference = Mathf.Abs(shouldValue - isValue);
            float percentage = (1f / shouldValue) * difference;

            if (percentage <= goodPercentDifference)
                return Color.green;
            else if (percentage >= goodPercentDifference && percentage <= mediumPercentDifference)
                return Color.yellow;
            else if (percentage >= mediumPercentDifference)
                return Color.red;
            else return Color.white;
        }

        public Canvas GetCanvas()
        {
            return canvas;
        }
    }
}



