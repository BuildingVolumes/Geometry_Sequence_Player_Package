using BuildingVolumes.Streaming;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class GeometrySequenceAPIExample : MonoBehaviour
{
    public string sequencePath = "";
    GeometrySequencePlayer player;

    int loopsPlayed = 0;

    // Start is called before the first frame update
    void Start()
    {
        //Get our player.
        player = GetComponent<GeometrySequencePlayer>();

        //First to load our sequence. In this case the path is inside of the Assets folder (in the Editor, this is also the Data Path),
        //so we set it as relative to our data path. We also set our desired target playback framerate here. 
        //Enabeling the autostart flag means that our media will play as soon as possible after being loaded
        player.OpenSequence(sequencePath, GeometrySequenceStream.PathType.RelativeToDataPath, 30, true);

        //Disable automatic looping
        player.SetLoopPlay(false);

        //Subscribe to the player events (optional, just needed if your app needs access to some of the events!)
        player.playbackEvents.AddListener(PlaybackEventListener);
    }

    // Update is called once per frame
    void Update()
    {
        //Get the total length ins seconds of our sequence
        float totalTime = player.GetTotalTime();

        //Check how much of our sequence has played
        float currentTime = player.GetCurrentTime();

        //If half of our sequence has played, we want to start the sequence from the beginning again for three times
        if (currentTime > totalTime / 2 && loopsPlayed < 3)
        {
            player.GoToTime(0);
            loopsPlayed++;
        }

    }

    //With this function, we can receive events, which give us information about the playback state.
    //You need to subscribe to the events first (here in the Start() function). You also need to
    //unsubscribe from the events, otherwise memory leaks occur (here in the OnDestroy() function).
    //The events are not really used in this example, but we print them out so that you can watch them unfold in the console.
    void PlaybackEventListener(GeometrySequencePlayer player, GeometrySequencePlayer.GSPlayerEvents events)
    {
        switch (events)
        {
            case GeometrySequencePlayer.GSPlayerEvents.PlaybackFinished:
                print("Playback Finished!");
                break;
            case GeometrySequencePlayer.GSPlayerEvents.PlaybackStarted:
                print("Playback Started!");
                break;
            case GeometrySequencePlayer.GSPlayerEvents.BufferingStarted:
                print("Buffering Started!");
                break;
            case GeometrySequencePlayer.GSPlayerEvents.BufferingCompleted:
                print("Buffering Completed!");
                break;
            case GeometrySequencePlayer.GSPlayerEvents.FrameDropped:
                print("Frame Dropped!");
                break;
            case GeometrySequencePlayer.GSPlayerEvents.Looped:
                print("Looped!");
                break;
        }
    }

    private void OnDestroy()
    {
        player.playbackEvents.RemoveListener(PlaybackEventListener);
    }
}
