using System;
using System.IO;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Jobs;
using Unity.Jobs;
using System.Text;
using System.Linq;
using System.Text.RegularExpressions;
using static BuildingVolumes.Streaming.SequenceConfiguration;


namespace BuildingVolumes.Streaming
{

    public class Frame
    {
        public Mesh.MeshDataArray meshArray; //When the data is a mesh
        public NativeArray<byte> vertexBufferRaw; //When the data is a pointcloud
        public NativeArray<byte> textureBufferRaw;

        public SequenceConfiguration.GeometryType geometryType;
        public SequenceConfiguration.TextureMode textureMode;

        public int headerSize;
        public int vertexCount;
        public int indiceCount;

        public ReadGeometryJob geoJob;
        public JobHandle geoJobHandle;
        public ReadTextureJob textureJob;
        public JobHandle textureJobHandle;

        public int playbackIndex;
        public bool wasConsumed;

        public Frame()
        {
            headerSize = 0;
            vertexCount = 0;
            indiceCount = 0;
            playbackIndex = 0;
            wasConsumed = true;
        }
    }

    public enum TextureMode { None, Single, PerFrame };

    public class BufferedGeometryReader
    {
        public string folder;
        public SequenceConfiguration sequenceConfig;
        public string[] plyFilePaths;
        public string[] texturesFilePath;
        public int bufferSize = 4;
        public int totalFrames = 0;

        public Frame[] frameBuffer;

        bool buffering = true;

        /// <summary>
        /// Create a new buffered reader. You must include a path to a valid folder
        /// </summary>
        /// <param name="folder">A path to a folder containing .ply geometry files and optionally .dds texture files</param>
        public BufferedGeometryReader(string folder, int frameBufferSize)
        {
            SetupReader(folder, frameBufferSize);
        }

        ~BufferedGeometryReader()
        {
            //When the reader is destroyed, we need to ensure that all the NativeArrays will also be manually deleted/disposed
            DisposeFrameBuffer(true);
        }

        /// <summary>
        /// Use this function to setup a new buffered Reader.
        /// </summary>
        /// <param name="folder">A path to a folder containing .ply geometry files and optionally .dds texture files</param>
        /// <returns>Returns true on success, false when any errors have occured during setup</returns>
        public bool SetupReader(string folder, int frameBufferSize)
        {
            this.folder = folder;

            sequenceConfig = LoadConfigFromFile(folder);
            if (sequenceConfig == null)
                return false;

            try
            {
                //Add a temporary padding to the file list, as otherwise the file order will be messed up
                plyFilePaths = new List<string>(Directory.GetFiles(folder, "*.ply")).OrderBy(file =>
                Regex.Replace(file, @"\d+", match => match.Value.PadLeft(9, '0'))).ToArray<string>();
            }

            catch (Exception e)
            {
                Debug.LogError("Sequence path is not valid or has restricted access! Path: " + folder + " Error: " + e.Message);
                return false;
            }

            if (plyFilePaths.Length == 0)
            {
                Debug.LogError("No .ply files in the sequence directory: " + folder);
                return false;
            }

            bufferSize = frameBufferSize;
            totalFrames = plyFilePaths.Length;
            frameBuffer = new Frame[bufferSize];

            for (int i = 0; i < frameBuffer.Length; i++)
            {
                frameBuffer[i] = new Frame();
                AllocateFrame(frameBuffer[i], sequenceConfig.geometryType, sequenceConfig.textureMode, sequenceConfig.maxVertexCount, sequenceConfig.maxIndiceCount, sequenceConfig.textureSize);
                frameBuffer[i].playbackIndex = -1;
            }

            return true;

        }

        /// <summary>
        /// Loads new frames in the buffer if there are free slots. Call this every frame
        /// </summary>
        public void BufferFrames(int currentPlaybackFrame)
        {
            if (!buffering)
                return;

            if (currentPlaybackFrame < 0 || currentPlaybackFrame > totalFrames)
                return;

            //Delete frames from buffer that are outside our current buffer range
            //which keeps our buffer clean in case of skips or lags
            //DeleteFramesOutsideOfBufferRange(currentPlaybackFrame);

            //Find out which frames we need to buffer. The buffer is a ring
            //buffer, so that when the playback loops, the whole clip doesn't need
            //to reload, but the frames should be ready.
            List<int> framesToBuffer = new List<int>();

            for (int i = 0; i < bufferSize; i++)
            {

                if (framesToBuffer.Count >= totalFrames)
                    continue;

                if (currentPlaybackFrame >= totalFrames)
                    currentPlaybackFrame = 0;

                if (GetBufferIndexForPlaybackIndex(currentPlaybackFrame) == -1)
                {
                    framesToBuffer.Add(currentPlaybackFrame);
                }

                currentPlaybackFrame++;
            }

            for (int i = 0; i < frameBuffer.Length; i++)
            {
                //Check if the buffer is ready to load the next frame 
                if (frameBuffer[i].wasConsumed && framesToBuffer.Count > 0)
                {
                    int newPlaybackIndex = framesToBuffer[0];

                    if (newPlaybackIndex < totalFrames)
                    {
                        Frame newFrame = frameBuffer[i];
                        newFrame.playbackIndex = newPlaybackIndex;

                        newFrame.headerSize = sequenceConfig.headerSizes[newPlaybackIndex];
                        newFrame.vertexCount = sequenceConfig.verticeCounts[newPlaybackIndex];
                        newFrame.indiceCount = sequenceConfig.indiceCounts[newPlaybackIndex];

                        newFrame = ScheduleGeometryReadJob(newFrame, plyFilePaths[newPlaybackIndex]);

                        //if (newFrame.textureMode == TextureMode.PerFrame && !newFrame.plyHeaderInfo.error)
                        //    newFrame = ScheduleTextureJob(newFrame, texturesFilePath[newPlaybackIndex]);

                        newFrame.wasConsumed = false;
                        frameBuffer[i] = newFrame;
                        framesToBuffer.Remove(newPlaybackIndex);
                    }
                }
            }

            JobHandle.ScheduleBatchedJobs();
        }

        public void LoadFrameImmediate(Frame frame, int index)
        {
            frame.headerSize = sequenceConfig.headerSizes[index];
            frame.vertexCount = sequenceConfig.verticeCounts[index];
            frame.indiceCount = sequenceConfig.indiceCounts[index];
            frame = ScheduleGeometryReadJob(frame, plyFilePaths[index]);
            frame.geoJobHandle.Complete();

            //if(sequenceConfig.textureMode != SequenceConfiguration.TextureMode.None)
            //{
            //    frame = ScheduleTextureJob(frame, texturesFilePath[index]);
            //    frame.textureJobHandle.Complete();
            //}

        }

        void AllocateFrame(Frame frame, GeometryType geoType, SequenceConfiguration.TextureMode textureMode, int maxVertexCount, int maxIndiceCount, int maxTextureSize)
        {
            VertexAttributeDescriptor[] layout = new VertexAttributeDescriptor[0];

            switch (sequenceConfig.geometryType)
            {
                case GeometryType.point:
                    layout = new[] {
                    new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
                    new VertexAttributeDescriptor(VertexAttribute.Color, VertexAttributeFormat.UNorm8, 4) };
                    break;

                case GeometryType.mesh:
                    layout = new[] { new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3) };
                    break;

                case GeometryType.texturedMesh:
                    layout = new[] { new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
                    new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 2) };
                    break;

                default:
                    break;
            }

            //Allocate every frame with the highest amount of vertices being used in this sequence. 
            //This way, we can re-use the meshArrays, instead of re-allocating them each frame
            frame.geometryType = geoType;
            frame.meshArray = Mesh.AllocateWritableMeshData(1);

            if (geoType == GeometryType.point)
            {
                frame.vertexBufferRaw = new NativeArray<byte>(maxVertexCount * 4 * 4, Allocator.Persistent);
            }

            else
            {
                //We still need to allocate the Array, even if it's not used
                frame.vertexBufferRaw = new NativeArray<byte>(1, Allocator.Persistent);
                frame.meshArray[0].SetVertexBufferParams(maxVertexCount, layout);
                frame.meshArray[0].SetIndexBufferParams(maxIndiceCount, IndexFormat.UInt32);
            }

            for (int i = 0; i < frameBuffer.Length; i++)
            {
                frame.textureMode = textureMode;

                if (textureMode == SequenceConfiguration.TextureMode.PerFrame)
                    frame.textureBufferRaw = new NativeArray<byte>(maxTextureSize, Allocator.Persistent);
                else
                    frame.textureBufferRaw = new NativeArray<byte>(1, Allocator.Persistent);
            }
        }


        /// <summary>
        /// Deletes frames that are either in the past of current Frame index,
        /// or too far in the future (the whole buffer size away from the current Frame Index)
        /// Should be regularily called to keep the buffer clean
        /// </summary>
        /// <param name="currentFrameIndex">The currently shown/played back frame</param>
        public void DeleteFramesOutsideOfBufferRange(int currentFrameIndex)
        {
            //We want to treat the buffer as a ring buffer, so that when we're looping,
            //the playback doesn't stutter. This means if the current Frame index is near
            //the end of the buffer, our buffer range extends to the beginning again

            //int minFrame = currentFrameIndex;
            //int maxframe = currentFrameIndex + bufferSize;
            //if (maxframe >= totalFrames)
            //    maxframe -= (totalFrames - 1);

            //for (int i = 0; i < frameBuffer.Length; i++)
            //{
            //    bool dispose = true;
            //    int playbackIndex = frameBuffer[i].playbackIndex;

            //    if (minFrame < maxframe)
            //    {
            //        if (playbackIndex >= minFrame && playbackIndex < maxframe)
            //            dispose = false;
            //    }

            //    else
            //    {
            //        if (playbackIndex >= maxframe || playbackIndex < maxframe)
            //            dispose = false;
            //    }

            //    if (dispose)
            //    {
            //        if (!frameBuffer[i].isDisposed)
            //        {
            //            bool disposed = DisposeFrame(i, false, false);
            //        }
            //    }

            //}
        }

        /// <summary>
        /// Check if the desired input frame of the sequence has already been buffered.
        /// </summary>
        /// <param name="playbackIndex">The desired frame number from the whole sequence</param>
        /// <returns>If the frame could be found and has been loaded, you get the index of the frame in the buffer. Returns -1 if frame could not be found or has not been loaded yet</returns>
        public int GetBufferIndexForLoadedPlaybackIndex(int playbackIndex)
        {
            for (int i = 0; i < frameBuffer.Length; i++)
            {
                if (frameBuffer[i].playbackIndex == playbackIndex)
                {
                    if (IsFrameBuffered(frameBuffer[i]))
                    {
                        return i;
                    }
                    else
                        return -1;
                }
            }

            return -1;
        }

        public int GetBufferIndexForPlaybackIndex(int playbackIndex)
        {
            for (int i = 0; i < frameBuffer.Length; i++)
            {
                if (frameBuffer[i].playbackIndex == playbackIndex)
                    return i;
            }

            return -1;
        }

        /// <summary>
        /// Get the total amount of frames that are fully stored in buffer
        /// After skipping or loading in a new sequence, it's useful to wait 
        /// until the buffer has stored at least a few frames
        /// </summary>
        /// <returns></returns>
        public int GetBufferedFrames()
        {
            int loadedFrames = 0;

            for (int i = 0; i < frameBuffer.Length; i++)
            {
                if (IsFrameBuffered(frameBuffer[i]))
                    loadedFrames++;
            }

            return loadedFrames;
        }

        /// <summary>
        /// Has the data loading finished for this frame?
        /// </summary>
        /// <param name="frame"></param>
        /// <returns></returns>
        public bool IsFrameBuffered(Frame frame)
        {
            if (frame.geoJobHandle.IsCompleted)
            {
                if (sequenceConfig.textureMode == SequenceConfiguration.TextureMode.PerFrame)
                {
                    if (frame.textureJobHandle.IsCompleted)
                        return true;
                }

                else
                    return true;
            }

            return false;
        }


        /// <summary>
        /// Schedules a Job that reads a .ply Pointcloud or mesh file from disk
        /// and loads it into memory.
        /// </summary>
        /// <param name="frame">The frame into which to load the data. The meshdataarray needs to be initialized already</param>
        /// <param name="plyPath">The absolute path to the .ply file </param>
        /// <returns></returns>
        public Frame ScheduleGeometryReadJob(Frame frame, string plyPath)
        {
            frame.geoJob = new ReadGeometryJob();
            frame.geoJob.pathCharArray = new NativeArray<byte>(Encoding.UTF8.GetBytes(plyPath), Allocator.Persistent);
            frame.geoJob.headerSize = frame.headerSize;
            frame.geoJob.vertexCount = frame.vertexCount;
            frame.geoJob.indiceCount = frame.indiceCount;
            frame.geoJob.vertexBuffer = frame.vertexBufferRaw;
            frame.geoJob.mesh = frame.meshArray[0];
            frame.geoJobHandle = frame.geoJob.Schedule(frame.geoJobHandle);

            return frame;

        }

        /// <summary>
        /// Schedules a job which loads a .dds DXT1 file from disk into memory
        /// </summary>
        /// <param name="frame">The frame data into which the texture will be loaded. The textureBufferRaw needs to be intialized already </param>
        /// <param name="texturePath"></param>
        /// <returns></returns>
        //public Frame ScheduleTextureJob(Frame frame, string texturePath)
        //{
        //    frame.textureJob = new ReadTextureJob();

        //    if (!frame.ddsHeaderInfo.error && frame.ddsHeaderInfo.size > 0)
        //    {
        //        if (frame.textureBufferRaw.Length != frame.ddsHeaderInfo.size)
        //        {
        //            frame.textureJobHandle.Complete();
        //            frame.textureBufferRaw = new NativeArray<byte>(frame.ddsHeaderInfo.size, Allocator.Persistent);
        //        }

        //        frame.textureJob.textureRawData = frame.textureBufferRaw;
        //        frame.textureJob.texturePathCharArray = new NativeArray<byte>(Encoding.UTF8.GetBytes(texturePath), Allocator.TempJob);

        //        frame.textureJobHandle = frame.textureJob.Schedule(frame.textureJobHandle);
        //    }

        //    return frame;
        //}


        /// <summary>
        /// This function ensures that all memory resources are unlocated
        /// and all jobs are finished, so that no memory leaks occur.
        /// </summary>
        public void DisposeFrameBuffer(bool stopBuffering)
        {
            buffering = !stopBuffering;

            if (frameBuffer != null)
            {
                for (int i = 0; i < frameBuffer.Length; i++)
                {
                    frameBuffer[i].geoJobHandle.Complete();
                    frameBuffer[i].vertexBufferRaw.Dispose();
                    frameBuffer[i].meshArray.Dispose();

                    frameBuffer[i].textureJobHandle.Complete();
                    frameBuffer[i].textureBufferRaw.Dispose();
                }
            }

            frameBuffer = null;

        }
    }

    public struct ReadGeometryJob : IJob
    {
        public NativeArray<byte> vertexBuffer;
        public Mesh.MeshData mesh;
        public bool readFinished;
        public int headerSize;
        public int vertexCount;
        public int indiceCount;
        public GeometryType geoType;

        [DeallocateOnJobCompletion]
        public NativeArray<byte> pathCharArray;

        public void Execute()
        {
            readFinished = false;

            //We can't give Lists/strings to a job directly, so we need this workaround 
            byte[] pathCharBuffer = new byte[pathCharArray.Length];
            pathCharArray.CopyTo(pathCharBuffer);
            string path = Encoding.UTF8.GetString(pathCharBuffer);

            //We read all bytes into a buffer at once, much quicker than doing it in many shorter reads.
            //This buffer only contains the raw mesh data without the header
            BinaryReader reader = new BinaryReader(new FileStream(path, FileMode.Open));
            reader.BaseStream.Position = headerSize;
            byte[] byteBuffer = reader.ReadBytes((int)(reader.BaseStream.Length - headerSize));
            reader.Close();
            reader.Dispose();

            if (geoType == GeometryType.point)
            {
                NativeArray<byte>.Copy(byteBuffer, vertexBuffer, byteBuffer.Length);
            }

            else
            {
                int vertexBufferSize;

                if (geoType == GeometryType.texturedMesh)
                    vertexBufferSize = vertexCount * 5;
                else
                    vertexBufferSize = vertexCount * 3;

                NativeArray<byte>.Copy(byteBuffer, mesh.GetVertexData<byte>(), vertexCount * vertexBufferSize * 4);

                int[] indicesRaw = new int[indiceCount];
                int facePositionInBuffer = vertexBufferSize * sizeof(float);
                int sizeOfIndexLine = sizeof(byte) + sizeof(int) * 3;

                //Reading the index is a bit more tricky because each index line contains the number of indices in that line, which we dont want to include
                for (int i = 0; i < indiceCount / 3; i++)
                {
                    Buffer.BlockCopy(byteBuffer, facePositionInBuffer + sizeOfIndexLine * i + sizeof(byte), indicesRaw, i * 3 * sizeof(int), 3 * sizeof(int));
                }

                NativeArray<int>.Copy(indicesRaw, mesh.GetIndexData<int>());
                mesh.subMeshCount = 1;
                mesh.SetSubMesh(0, new SubMeshDescriptor(0, indicesRaw.Length, MeshTopology.Triangles));
            }

            readFinished = true;
        }
    }

    public struct ReadTextureJob : IJob
    {
        public NativeArray<byte> textureRawData;
        public bool readFinished;

        [DeallocateOnJobCompletion]
        public NativeArray<byte> texturePathCharArray;

        public void Execute()
        {
            readFinished = false;
            string texturePath = "";

            byte[] texturePathCharBuffer = new byte[texturePathCharArray.Length];
            texturePathCharArray.CopyTo(texturePathCharBuffer);
            texturePath = Encoding.UTF8.GetString(texturePathCharBuffer);

            int DDS_HEADER_SIZE = 128;

            BinaryReader textureReader = new BinaryReader(new FileStream(texturePath, FileMode.Open));

            //As GPUs can access .DDS data directly, we can simply take the binary blob and upload it to the GPU
            textureReader.BaseStream.Position = DDS_HEADER_SIZE; //Skip the DDS header
            textureRawData.CopyFrom(textureReader.ReadBytes((int)textureReader.BaseStream.Length - DDS_HEADER_SIZE));
            textureReader.Close();

            readFinished = true;
        }
    }
}
