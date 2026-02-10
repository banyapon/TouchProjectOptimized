using UnityEngine;
using TMPro;

public class UIDebugClass : MonoBehaviour
{
    public static UIDebugClass Instance;

    [Header("UI References")]
    public TMP_Text touchLogText;       // แสดง touch data (LogTouchData)
    public TMP_Text movementLogText;    // แสดง movement data (position, speed, gesture)
    public TMP_Text gestureLogText;     // แสดง gesture state (Hold/Drag/Swipe)

    [Header("UI Toggle")]
    public GameObject panelDebug, sliderSpeed;
    private bool isToggle = false;
    public bool IsDebugActive => isToggle;
    void Awake()
    {
        Instance = this;
        panelDebug.SetActive(false);
        sliderSpeed.SetActive(false);
    }

    public void ToggleDebug()
    {
        isToggle = !isToggle;
    }

    public void Update()
    {
        if(isToggle)
        {
            panelDebug.SetActive(true);
            sliderSpeed.SetActive(true);
        }
        else
        {
            panelDebug.SetActive(false);
            sliderSpeed.SetActive(false);
        }
    }
    public void SetTouchLog(string msg)
    {
        if (touchLogText != null)
            touchLogText.text = msg;
    }

    public void SetMovementLog(string msg)
    {
        if (movementLogText != null)
            movementLogText.text = msg;
    }

    public void SetGestureLog(string msg)
    {
        if (gestureLogText != null)
            gestureLogText.text = msg;
    }
}
