using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using BuildingVolumes.Streaming;

public class GSFrameViz : MonoBehaviour
{
    public TextMeshProUGUI bufferFrameNumber;
    public TextMeshProUGUI playbackFrameNumber;
    public UnityEngine.UI.Image image;

    public void SetBufferState(BufferState state)
    {
        switch(state)
                    {
                        case BufferState.Empty:
                        image.color = Color.black;
                        break;
                        case BufferState.Consumed:
                        image.color = Color.gray;
                        break;
                        case BufferState.Reading:
                        image.color = Color.red;
                        break;
                        case BufferState.Loading:
                        image.color = Color.yellow;
                        break;
                        case BufferState.Ready:
                        image.color = Color.green;
                        break;

                    }
    }

    public void SetAsShown()
    {
        image.color = Color.white;
    }


    public void SetBufferFrameNumber(int value)
    {
        bufferFrameNumber.text = value.ToString();
    }

    public void SetPlaybackFrameNumber(int value)
    {
        playbackFrameNumber.text = value.ToString();
    }
}
