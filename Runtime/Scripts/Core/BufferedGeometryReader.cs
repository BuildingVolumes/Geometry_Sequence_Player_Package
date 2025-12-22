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
using static BuildingVolumes.Player.SequenceConfiguration;
using Unity.IO.LowLevel.Unsafe;
using Unity.Collections.LowLevel.Unsafe;
using System.Threading;

namespace BuildingVolumes.Player
{
  public class Frame
  {
    public NativeArray<byte> vertexBufferRaw;
    public NativeArray<byte> indiceBufferRaw;
    public NativeArray<byte> indiceIntermediateBuffer;
    public NativeArray<byte> textureBufferRaw;

    public SequenceConfiguration sequenceConfiguration;

    public ReadGeometryJob geoJob;
    public JobHandle geoJobHandle;
    public ReadTextureJob textureJob;
    public JobHandle textureJobHandle;
    public PostProcessJob postProcessJob;
    public JobHandle postProcessJobHandle;

    public BufferState bufferState = BufferState.Empty;
    public int readBufferSize;
    public int playbackIndex;
    public float finishedBufferingTime;

    public Frame()
    {
      playbackIndex = 0;
      bufferState = BufferState.Empty;
    }
  }

  public enum TextureMode { None, Single, PerFrame };

  public enum BufferState { Empty, Consumed, Reading, Loading, Ready, Playing }

  public class BufferedGeometryReader
  {
    public string folder;
    public SequenceConfiguration sequenceConfig;
    public string[] plyFilePaths;
    public string[] texturesFilePathDDS;
    public string[] texturesFilePathASTC;
    public int bufferSize = 4;
    public int totalFrames = 0;
    public int frameCount = 0;
    public Frame[] frameBuffer;

    public GameObject streamParent;
    public Material materialSource;

    bool buffering = true;

    /// <summary>
    /// Create a new buffered reader. 
    /// </summary>
    public BufferedGeometryReader()
    {
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

      if (plyFilePaths.Length != sequenceConfig.verticeCounts.Count)
      {
        Debug.LogError("Could not find all required .ply files, make sure your sequence doesn't miss any!");
        return false;
      }

      if (sequenceConfig.textureMode != SequenceConfiguration.TextureMode.None)
      {
        if (sequenceConfig.DDS && GetDeviceDependentTextureFormat() == SequenceConfiguration.TextureFormat.DDS)
        {
          try
          {
            //Add a temporary padding to the file list, as otherwise the file order will be messed up
            texturesFilePathDDS = new List<string>(Directory.GetFiles(folder, "*.dds")).OrderBy(file =>
            Regex.Replace(file, @"\d+", match => match.Value.PadLeft(9, '0'))).ToArray<string>();
          }

          catch (Exception e)
          {
            Debug.LogError("Sequence path is not valid or has restricted access! Path: " + folder + " Error: " + e.Message);
            return false;
          }

          if (texturesFilePathDDS.Length == 0)
          {
            Debug.LogError("No .dds texture files (for desktop devices) could be found! Make sure that you converted and uploaded .dds textures for this device!");
            return false;
          }

          if (sequenceConfig.textureMode == SequenceConfiguration.TextureMode.PerFrame)
          {
            if (texturesFilePathDDS.Length != sequenceConfig.verticeCounts.Count)
            {
              Debug.LogError("Could not find all required .dds texture files, make sure your sequence doesn't miss any!");
              return false;
            }
          }
        }

        if (sequenceConfig.ASTC && GetDeviceDependentTextureFormat() == SequenceConfiguration.TextureFormat.ASTC)
        {
          try
          {
            //Add a temporary padding to the file list, as otherwise the file order will be messed up
            texturesFilePathASTC = new List<string>(Directory.GetFiles(folder, "*.astc")).OrderBy(file =>
            Regex.Replace(file, @"\d+", match => match.Value.PadLeft(9, '0'))).ToArray<string>();
          }

          catch (Exception e)
          {
            Debug.LogError("Sequence path is not valid or has restricted access! Path: " + folder + " Error: " + e.Message);
            return false;
          }

          if (texturesFilePathASTC.Length == 0)
          {
            Debug.LogError("No .astc texture files (for mobile devices) could be found! Make sure that you converted and uploaded .astc texture files to this device!");
            return false;
          }

          if (sequenceConfig.textureMode == SequenceConfiguration.TextureMode.PerFrame)
          {
            if (texturesFilePathASTC.Length != sequenceConfig.verticeCounts.Count)
            {
              Debug.LogError("Could not find all required .atsc texture files, make sure your sequence doesn't miss any!");
              return false;
            }
          }

#if UNITY_EDITOR
          if (texturesFilePathASTC.Length > 0 && texturesFilePathDDS.Length == 0)
          {
            Debug.LogError("Only .astc texture files for mobile devices have been found in your sequence." +
                "Astc Textures cannot not be displayed in the editor. To display textures in the editor" +
                "please additionally generate .dds textures with the converter utility!");
          }
#endif
        }



      }

      bufferSize = frameBufferSize;
      totalFrames = plyFilePaths.Length;


      if (bufferSize > totalFrames)
        bufferSize = totalFrames - 1;

      if (bufferSize < 1)
        bufferSize = 1;

      frameBuffer = new Frame[bufferSize];

      for (int i = 0; i < frameBuffer.Length; i++)
      {
        frameBuffer[i] = new Frame();
        frameBuffer[i].sequenceConfiguration = sequenceConfig;
        AllocateFrame(frameBuffer[i], sequenceConfig, i);
        frameBuffer[i].playbackIndex = -1;
      }

      return true;

    }

    /// <summary>
    /// Loads new frames in the buffer if there are free slots. Call this every frame
    /// </summary>
    public void BufferFrames(int targetPlaybackIndex, int lastPlaybackIndex)
    {

      if (!buffering)
        return;

      if (targetPlaybackIndex < 0 || targetPlaybackIndex > totalFrames)
        return;

      //Mark frames from buffer that are outside our current buffer range
      //as okay to be overwritten, which keeps our buffer moving forward in case of skips or lags
      DeletePastFrames(targetPlaybackIndex, lastPlaybackIndex);

      //Find out which frames we need to buffer. The buffer is a ring
      //buffer, so that when the playback loops, the whole clip doesn't need
      //to reload, but the frames should be ready.
      List<int> framesToBuffer = new List<int>();

      //Look for the frames we could potentially buffer
      for (int i = 0; i < totalFrames; i++)
      {
        //In case our buffer is larger than the whole sequence
        if (framesToBuffer.Count >= totalFrames)
          continue;

        if (targetPlaybackIndex >= totalFrames)
          targetPlaybackIndex = 0;

        int bufferIndex = GetBufferIndex(targetPlaybackIndex);
        if (bufferIndex == -1) //The frame is not already buffered
        {
          framesToBuffer.Add(targetPlaybackIndex);
          //Debug.Log("Buffer requested for frame " + targetPlaybackIndex + " in buffer " + i);
        }

        targetPlaybackIndex++;
      }

      //Check if we have any free buffer space to buffer more frames
      for (int i = 0; i < frameBuffer.Length; i++)
      {
        //Check if the buffer is ready to load the next frame 
        if ((frameBuffer[i].bufferState == BufferState.Consumed || frameBuffer[i].bufferState == BufferState.Empty) && framesToBuffer.Count > 0)
        {
          int newPlaybackIndex = framesToBuffer[0];

          if (newPlaybackIndex < totalFrames)
          {
            //Debug.Log("Buffering Frame: " + newPlaybackIndex + " at buffer " + i);
            ScheduleFrame(frameBuffer[i], newPlaybackIndex);
            framesToBuffer.Remove(newPlaybackIndex);
          }
        }

      }

      JobHandle.ScheduleBatchedJobs();

      CheckFramesForCompletion();
    }


    public void ScheduleFrame(Frame frame, int newPlaybackIndex)
    {
      SetupFrameForReading(frame, sequenceConfig, newPlaybackIndex);
      ScheduleGeometryReadJob(frame, plyFilePaths[newPlaybackIndex]);
      if (sequenceConfig.textureMode == SequenceConfiguration.TextureMode.PerFrame)
        ScheduleTextureReadJob(frame, GetDeviceDependendentTexturePath(newPlaybackIndex));

    }

    public void CheckFramesForCompletion()
    {
      foreach (Frame frame in frameBuffer)
      {
        if (IsFrameBuffered(frame) && frame.bufferState == BufferState.Reading)
          frame.bufferState = BufferState.Ready;
      }
    }

    public void SetupFrameForReading(Frame frame, SequenceConfiguration config, int index)
    {
      frame.sequenceConfiguration = config;
      frame.playbackIndex = index;
      frame.bufferState = BufferState.Reading;
    }

    void AllocateFrame(Frame frame, SequenceConfiguration config, int bufferIndex)
    {
      frame.sequenceConfiguration.geometryType = config.geometryType;
      frame.geoJob = new ReadGeometryJob();
      frame.textureJob = new ReadTextureJob();

      //Allocate every frame with the highest amount of vertices and indices being used in this sequence. 
      //This way, we can re-use the meshArrays, instead of re-allocating them each frame
      if (config.geometryType == GeometryType.point)
      {
        int vertexSizeBytes = 4 * 4; //3 vertex position floats + 1 byte of color
        if (config.hasNormals)
          vertexSizeBytes += 3 * 4; //3 vertex normal floats

        frame.vertexBufferRaw = new NativeArray<byte>(config.maxVertexCount * vertexSizeBytes, Allocator.Persistent);
        frame.indiceBufferRaw = new NativeArray<byte>(1, Allocator.Persistent);
        frame.indiceIntermediateBuffer = new NativeArray<byte>(1, Allocator.Persistent);
      }

      else
      {
        int vertexSizeBytes = 3 * 4; //3 vertex position floats
        if(config.hasUVs)
          vertexSizeBytes += 2 * 4; //2 UV coordinate floats
        if(config.hasNormals)
          vertexSizeBytes += 3 * 4; //3 vertex normal floats

        frame.vertexBufferRaw = new NativeArray<byte>(config.maxVertexCount * vertexSizeBytes, Allocator.Persistent);
        frame.indiceBufferRaw = new NativeArray<byte>(config.maxIndiceCount * 4, Allocator.Persistent);
        frame.indiceIntermediateBuffer = new NativeArray<byte>((config.maxIndiceCount * 4) + config.maxIndiceCount, Allocator.Persistent);
      }

      frame.sequenceConfiguration.textureMode = config.textureMode;

      if (config.textureMode == SequenceConfiguration.TextureMode.PerFrame)
        frame.textureBufferRaw = new NativeArray<byte>(GetDeviceDependentTextureSize(config), Allocator.Persistent);
      else if (config.textureMode == SequenceConfiguration.TextureMode.Single && frame.playbackIndex == 0)
        frame.textureBufferRaw = new NativeArray<byte>(GetDeviceDependentTextureSize(config), Allocator.Persistent);
      else
        frame.textureBufferRaw = new NativeArray<byte>(1, Allocator.Persistent);

    }

    /// <summary>
    /// Marks frames that are in the past of current Frame index
    /// Should be regularily called to keep the buffer clean in case of skips/lag
    /// </summary>
    /// <param name="targetPlaybackIndex">The currently shown/played back frame</param>
    public void DeletePastFrames(int targetPlaybackIndex, int lastPlaybackIndex)
    {
      //We want to keep all frames in the buffer, which are one buffersize ahead of
      //the target Frame, as these will be played soon. Outside of that range, all frames can be deleted 
      int targetMaxFrame = targetPlaybackIndex + bufferSize;
      if (targetMaxFrame >= totalFrames)
        targetMaxFrame = targetMaxFrame % totalFrames;

      for (int i = 0; i < frameBuffer.Length; i++)
      {
        bool dispose = false;
        int playbackIndex = frameBuffer[i].playbackIndex;

        //If the target max frame has already looped around the ringbuffer
        if (targetMaxFrame < targetPlaybackIndex)
        {
          if (playbackIndex < targetPlaybackIndex && playbackIndex > targetMaxFrame)
            dispose = true;
        }

        else
        {
          if (playbackIndex < targetPlaybackIndex || playbackIndex > targetMaxFrame)
            dispose = true;
        }

        if (dispose)
        {
          if (playbackIndex != lastPlaybackIndex)
          {
            frameBuffer[i].bufferState = BufferState.Empty;
            //Debug.Log("Deleting frame " + playbackIndex + " from Buffer " + i);
          }
        }
      }
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
          if (frameBuffer[i].bufferState == BufferState.Ready)
          {
            return i;
          }
          else
            return -1;
        }
      }

      return -1;
    }

    public int GetBufferIndex(int playbackIndex)
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


    public int GetDeviceDependentTextureSize(SequenceConfiguration configuration)
    {
      SequenceConfiguration.TextureFormat format = GetDeviceDependentTextureFormat();

      switch (format)
      {
        case SequenceConfiguration.TextureFormat.DDS:
          return configuration.textureSizeDDS;
        case SequenceConfiguration.TextureFormat.ASTC:
          return configuration.textureSizeASTC;
        case SequenceConfiguration.TextureFormat.NotSupported:
        default:
          return 0;
      }
    }

    public string GetDeviceDependendentTexturePath(int playbackIndex)
    {
      SequenceConfiguration.TextureFormat format = GetDeviceDependentTextureFormat();

      switch (format)
      {
        case SequenceConfiguration.TextureFormat.DDS:
          if (texturesFilePathDDS == null)
            return "";
          if (texturesFilePathDDS.Length < playbackIndex)
            return "";
          return texturesFilePathDDS[playbackIndex];
        case SequenceConfiguration.TextureFormat.ASTC:
          if (texturesFilePathASTC == null)
            return "";
          if (texturesFilePathASTC.Length < playbackIndex)
            return "";
          return texturesFilePathASTC[playbackIndex];
        case SequenceConfiguration.TextureFormat.NotSupported:
        default:
          return "";
      }
    }




    /// <summary>
    /// Schedules a Job that reads a .ply Pointcloud or mesh file from disk
    /// and loads it into memory.
    /// </summary>
    /// <param name="frame">The frame into which to load the data. The meshdataarray needs to be initialized already</param>
    /// <param name="plyPath">The absolute path to the .ply file </param>
    /// <returns></returns>
    public void ScheduleGeometryReadJob(Frame frame, string plyPath)
    {
      frame.geoJob.pathCharArray = new NativeArray<byte>(Encoding.UTF8.GetBytes(plyPath), Allocator.Persistent);
      frame.geoJob.readCmd = new NativeArray<ReadCommand>(1, Allocator.Persistent);
      frame.geoJob.geoType = frame.sequenceConfiguration.geometryType;
      frame.geoJob.hasUVs = frame.sequenceConfiguration.hasUVs;
      frame.geoJob.hasNormals = frame.sequenceConfiguration.hasNormals;
      frame.geoJob.headerSize = frame.sequenceConfiguration.headerSizes[frame.playbackIndex];
      frame.geoJob.vertexCount = frame.sequenceConfiguration.verticeCounts[frame.playbackIndex];
      frame.geoJob.indiceCount = frame.sequenceConfiguration.indiceCounts[frame.playbackIndex];
      frame.geoJob.maxIndiceCount = frame.sequenceConfiguration.maxIndiceCount;
      frame.geoJob.vertexBuffer = frame.vertexBufferRaw;
      frame.geoJob.indiceBuffer = frame.indiceBufferRaw;
      frame.geoJob.indiceIntermediateBuffer = frame.indiceIntermediateBuffer;
      frame.geoJobHandle = frame.geoJob.Schedule(frame.geoJobHandle);
    }

    /// <summary>
    /// Schedules a job which loads a texture file from disk into memory
    /// </summary>
    /// <param name="frame">The frame data into which the texture will be loaded. The textureBufferRaw needs to be intialized already </param>
    /// <param name="texturePath"></param>
    /// <returns></returns>
    public void ScheduleTextureReadJob(Frame frame, string texturePath)
    {
      if (texturePath == "")
      {
        Debug.LogError("Texture Path to read is empty!");
        return;
      }

      frame.textureJob.readCmd = new NativeArray<ReadCommand>(1, Allocator.Persistent);
      frame.textureJob.format = GetDeviceDependentTextureFormat();
      frame.textureJob.textureSize = GetDeviceDependentTextureSize(frame.sequenceConfiguration);
      frame.textureJob.textureRawData = frame.textureBufferRaw;
      frame.textureJob.texturePathCharArray = new NativeArray<byte>(Encoding.UTF8.GetBytes(texturePath), Allocator.Persistent);
      frame.textureJobHandle = frame.textureJob.Schedule(frame.textureJobHandle);
    }


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
          frameBuffer[i].indiceBufferRaw.Dispose();
          frameBuffer[i].indiceIntermediateBuffer.Dispose();

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
    public NativeArray<byte> indiceIntermediateBuffer;
    public NativeArray<byte> indiceBuffer;
    public bool hasUVs;
    public bool hasNormals;
    public bool readFinished;
    public int headerSize;
    public int vertexCount;
    public int indiceCount;
    public int maxIndiceCount;
    public GeometryType geoType;

    [DeallocateOnJobCompletion]
    public NativeArray<byte> pathCharArray;

    [DeallocateOnJobCompletion]
    public NativeArray<ReadCommand> readCmd;

    public void Execute()
    {
      readFinished = false;

      //We can't give Lists/strings to a job directly, so we need this workaround 
      byte[] pathCharBuffer = new byte[pathCharArray.Length];
      pathCharArray.CopyTo(pathCharBuffer);
      string path = Encoding.UTF8.GetString(pathCharBuffer);

      ReadCommand readVerticesCmd;
      ReadHandle readVerticesHandle;
      readVerticesCmd.Offset = headerSize;
      unsafe { readVerticesCmd.Buffer = NativeArrayUnsafeUtility.GetUnsafePtr<byte>(vertexBuffer); }

      if (geoType == GeometryType.point)
      {
        readVerticesCmd.Size = 3 * 4; // Vertex Coords 
        readVerticesCmd.Size += 4; // Vertex Color
      }
      else
        readVerticesCmd.Size = 3 * 4;

      if (hasUVs)
        readVerticesCmd.Size += 2 * 4;
      if (hasNormals)
        readVerticesCmd.Size += 3 * 4;

      readVerticesCmd.Size *= vertexCount;

      readCmd[0] = readVerticesCmd;

      unsafe { readVerticesHandle = AsyncReadManager.Read(path, (ReadCommand*)readCmd.GetUnsafePtr(), 1); }

      if (readVerticesHandle.IsValid())
      {
        while (readVerticesHandle.Status == ReadStatus.InProgress)
        {
          Thread.Sleep(1);
        }
      }

      readVerticesHandle.Dispose();

      if (geoType != GeometryType.point)
      {
        //Reading the index is a bit more tricky because each index line contains the number of indices in that line, which we dont want to include
        //So we first read it into a temporary array, and then copy only the indices

        ReadCommand readIndicesCmd;
        ReadHandle readIndicesHandle;
        readIndicesCmd.Offset = headerSize + readVerticesCmd.Size;
        readIndicesCmd.Size = (indiceCount * 4) + indiceCount;
        unsafe { readIndicesCmd.Buffer = NativeArrayUnsafeUtility.GetUnsafePtr<byte>(indiceIntermediateBuffer); }
        readCmd[0] = readIndicesCmd;
        unsafe { readIndicesHandle = AsyncReadManager.Read(path, (ReadCommand*)readCmd.GetUnsafePtr(), 1); }

        if (readIndicesHandle.IsValid())
        {
          while (readIndicesHandle.Status == ReadStatus.InProgress)
          {
            Thread.Sleep(1);
          }
        }

        readIndicesHandle.Dispose();

        int indiceTriplet = 3 * 4;

        for (int i = 0; i < indiceCount / 3; i++)
        {
          NativeArray<byte>.Copy(indiceIntermediateBuffer, (i * (indiceTriplet + 1)) + 1, indiceBuffer, i * indiceTriplet, indiceTriplet);
        }
      }

      readFinished = true;
    }
  }

  public struct ReadTextureJob : IJob
  {
    public NativeArray<byte> textureRawData;
    public int textureSize;
    public bool readFinished;
    public SequenceConfiguration.TextureFormat format;

    [DeallocateOnJobCompletion]
    public NativeArray<byte> texturePathCharArray;

    [DeallocateOnJobCompletion]
    public NativeArray<ReadCommand> readCmd;

    public void Execute()
    {
      readFinished = false;
      string texturePath = "";

      byte[] texturePathCharBuffer = new byte[texturePathCharArray.Length];
      texturePathCharArray.CopyTo(texturePathCharBuffer);
      texturePath = Encoding.UTF8.GetString(texturePathCharBuffer);

      int headerSize = 0;
      if (format == SequenceConfiguration.TextureFormat.DDS)
        headerSize = 128;
      if (format == SequenceConfiguration.TextureFormat.ASTC)
        headerSize = 16;

      ReadCommand readTextureCmd;
      ReadHandle readTextureHandle;
      readTextureCmd.Offset = headerSize;
      readTextureCmd.Size = textureSize;
      unsafe { readTextureCmd.Buffer = NativeArrayUnsafeUtility.GetUnsafePtr<byte>(textureRawData); }


      readCmd[0] = readTextureCmd;

      unsafe { readTextureHandle = AsyncReadManager.Read(texturePath, (ReadCommand*)readCmd.GetUnsafePtr(), 1); }

      if (readTextureHandle.IsValid())
      {
        while (readTextureHandle.Status == ReadStatus.InProgress)
        {
          Thread.Sleep(1);
        }
      }

      readTextureHandle.Dispose();

      readFinished = true;
    }
  }

  public struct PostProcessJob : IJob
  {
    public void Execute()
    {
      
    }
  }
}
