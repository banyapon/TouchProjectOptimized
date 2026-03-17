using UnityEngine;

public class FollowCamera : MonoBehaviour
{
    [Header("Target")]
    [SerializeField] private string targetTag = "Player";
    [SerializeField] private Transform target;

    [Header("Follow Settings")]
    [SerializeField] private Vector3 offset;
    [SerializeField] private bool useSceneOffset = true;
    [SerializeField] private bool followOnLateUpdate = true;

    private void Start()
    {
        FindTarget();

        if (target != null && useSceneOffset)
            offset = transform.position - target.position;

        FollowTarget();
    }

    private void Update()
    {
        if (!followOnLateUpdate)
            FollowTarget();
    }

    private void LateUpdate()
    {
        if (followOnLateUpdate)
            FollowTarget();
    }

    private void FindTarget()
    {
        if (target != null)
            return;

        GameObject playerObject = GameObject.FindGameObjectWithTag(targetTag);
        if (playerObject != null)
            target = playerObject.transform;
    }

    private void FollowTarget()
    {
        if (target == null)
        {
            FindTarget();
            if (target == null)
                return;
        }

        transform.position = target.position + offset;
    }
}
