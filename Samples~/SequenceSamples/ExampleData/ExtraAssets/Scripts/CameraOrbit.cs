// This script is a modified version of:

//  A simple Unity C# script for orbital movement around a target gameobject
//  Author: Ashkan Ashtiani: https://github.com/3dln
//  Gist on Github: https://gist.github.com/3dln/c16d000b174f7ccf6df9a1cb0cef7f80

using System;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public class CameraOrbit : MonoBehaviour
{
    public GameObject target;
    public float distance = 10.0f;

    public float xSpeed = 250.0f;
    public float ySpeed = 120.0f;

    public float yMinLimit = -20;
    public float yMaxLimit = 80;

    float x = 0.0f;
    float y = 0.0f;

    bool autoRotate = true;
    float autoRotateTimer = 5;
    float timeSinceInteraction = 0;

    void Start()
    {
        var angles = transform.eulerAngles;
        x = angles.y;
        y = angles.x;
    }

    float prevDistance;

    void LateUpdate()
    {
        Vector2 mousePosition = Vector2.zero;
        float mouseScrollDelta = 0;
        float mouseXDelta = 0;
        float mouseYDelta = 0;
        bool mouseLeftButton = false;
        bool mouseRightButton = false;

#if ENABLE_INPUT_SYSTEM
        mousePosition = Mouse.current.position.value;
        mouseScrollDelta = Mouse.current.scroll.value.y;
        mouseXDelta = Mouse.current.delta.x.value / 10;
        mouseYDelta = Mouse.current.delta.y.value / 10;
        mouseLeftButton = Mouse.current.leftButton.value > 0.5 ? true: false;
        mouseRightButton = Mouse.current.rightButton.isPressed;
#endif

#if ENABLE_LEGACY_INPUT_MANAGER
        mousePosition = Input.mousePosition;
        mouseScrollDelta = Input.GetAxis("Mouse ScrollWheel");
        mouseXDelta = Input.GetAxis("Mouse X");
        mouseYDelta = Input.GetAxis("Mouse Y");
        mouseLeftButton = Input.GetMouseButton(0);
        mouseRightButton = Input.GetMouseButton(1);
#endif

        if (distance < 0) distance = 0;
        distance -= mouseScrollDelta * 2;


        if (target && (mouseLeftButton || mouseRightButton))
        {
            autoRotate = false;
            timeSinceInteraction = Time.time;

            var dpiScale = 1f;
            if (Screen.dpi < 1) dpiScale = 1;
            if (Screen.dpi < 200) dpiScale = 1;
            else dpiScale = Screen.dpi / 200f;

            if(mousePosition.x < 0 || mousePosition.x > Screen.width || mousePosition.y < 0 || mousePosition.y > Screen.height)
                return;


            // comment out these two lines if you don't want to hide mouse curser or you have a UI button 
            Cursor.visible = false;
            Cursor.lockState = CursorLockMode.Locked;

            x += mouseXDelta * xSpeed * 0.02f;
            y -= mouseYDelta * ySpeed * 0.02f;
        }

        else
        {
            if (Time.time - timeSinceInteraction > autoRotateTimer)
                autoRotate = true;

            if (autoRotate)
                x += 0.01f * xSpeed * 0.02f;

            // comment out these two lines if you don't want to hide mouse curser or you have a UI button 
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;
        }

        y = ClampAngle(y, yMinLimit, yMaxLimit);
        var rotation = Quaternion.Euler(y, x, 0);
        var position = rotation * new Vector3(0.0f, 0.0f, -distance) + target.transform.position;
        transform.rotation = rotation;
        transform.position = position;

        if (Math.Abs(prevDistance - distance) > 0.001f)
        {
            prevDistance = distance;
            var rot = Quaternion.Euler(y, x, 0);
            var po = rot * new Vector3(0.0f, 0.0f, -distance) + target.transform.position;
            transform.rotation = rot;
            transform.position = po;
        }
    }

    static float ClampAngle(float angle, float min, float max)
    {
        if (angle < -360)
            angle += 360;
        if (angle > 360)
            angle -= 360;
        return Mathf.Clamp(angle, min, max);
    }
}
