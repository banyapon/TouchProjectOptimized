using UnityEngine;
using UnityEngine.UI;
using System;
using System.IO;

public class LogDataClass : MonoBehaviour
{
    public static LogDataClass Instance;

    private StreamWriter writer;

    void Awake()
    {
        Instance = this;
    }

    void Start()
    {
        writer = new StreamWriter("data.log", true);
    }

 
    /// Log touch data - เรียกจาก DogPaddle, DragnGo, StreetView
    public void LogTouch(string identifier, Touch touch)
    {
        string formattedTime = DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss:fff");
        string msg = string.Format(
            "{0},{1},{2},{3},{4},{5},{6}",
            identifier,
            touch.fingerId,
            touch.position,
            touch.deltaPosition,
            touch.phase,
            touch.tapCount,
            formattedTime
        );

        if (writer != null)
        {
            writer.WriteLine(msg);
            writer.Flush();
        }

        Debug.Log(msg);

        // ส่งให้ UIDebugClass แสดงบน UI
        if (UIDebugClass.Instance != null)
            UIDebugClass.Instance.SetTouchLog(msg);
    }

    /// <summary>
    /// Log movement data - เรียกเมื่อมีการเคลื่อนที่
    /// </summary>
    public void LogMovement(string identifier, Vector3 position, float speed, string gesture)
    {
        string formattedTime = DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss:fff");
        string msg = string.Format(
            "{0},pos={1},speed={2:F2},gesture={3},{4}",
            identifier,
            position,
            speed,
            gesture,
            formattedTime
        );

        if (writer != null)
        {
            writer.WriteLine(msg);
            writer.Flush();
        }

        Debug.Log(msg);

        if (UIDebugClass.Instance != null)
            UIDebugClass.Instance.SetMovementLog(msg);
    }

    void OnDestroy()
    {
        if (writer != null)
        {
            writer.Close();
            writer = null;
        }
    }
}
