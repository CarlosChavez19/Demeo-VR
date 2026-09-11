using UnityEngine;
using Fusion;

public class NetworkVRRig : NetworkBehaviour
{
    [Header("Visuales de Red (Lo que verán los demás)")]
    [SerializeField] private Transform _networkHeadVisual;
    [SerializeField] private Transform _networkLeftHandVisual;
    [SerializeField] private Transform _networkRightHandVisual;

    private Transform _localHead;
    private Transform _localLeftHand;
    private Transform _localRightHand;

    [Networked] private Vector3 HeadPosition { get; set; }
    [Networked] private Quaternion HeadRotation { get; set; }

    [Networked] private Vector3 LeftHandPosition { get; set; }
    [Networked] private Quaternion LeftHandRotation { get; set; }

    [Networked] private Vector3 RightHandPosition { get; set; }
    [Networked] private Quaternion RightHandRotation { get; set; }

    public override void Spawned()
    {
        // OJO:
        // InputAuthority = este avatar es mío.
        // StateAuthority = quién controla el estado en la red.
        // En Host Mode, el host tiene StateAuthority de casi todo.
        if (Object.HasInputAuthority)
        {
            BuscarReferenciasLocales();
            OcultarMisVisualesLocales();
            TeletransportarRigLocalASpawn();

            Debug.Log("NetworkVRRig: Este avatar pertenece al jugador local.");
        }
        else
        {
            Debug.Log("NetworkVRRig: Este avatar es de otro jugador.");
        }
    }

    public override void FixedUpdateNetwork()
    {
        if (!Object.HasInputAuthority)
            return;

        if (_localHead == null || _localLeftHand == null || _localRightHand == null)
        {
            BuscarReferenciasLocales();
        }

        Vector3 headPos = _localHead != null ? _localHead.position : transform.position;
        Quaternion headRot = _localHead != null ? _localHead.rotation : transform.rotation;

        Vector3 leftPos = _localLeftHand != null ? _localLeftHand.position : transform.position;
        Quaternion leftRot = _localLeftHand != null ? _localLeftHand.rotation : transform.rotation;

        Vector3 rightPos = _localRightHand != null ? _localRightHand.position : transform.position;
        Quaternion rightRot = _localRightHand != null ? _localRightHand.rotation : transform.rotation;

        // Si soy host y además este avatar es mío, puedo escribir directo.
        if (Object.HasStateAuthority)
        {
            ActualizarDatosDeRed(
                headPos,
                headRot,
                leftPos,
                leftRot,
                rightPos,
                rightRot
            );
        }
        else
        {
            // Si soy cliente, mando mis datos al host.
            RPC_ActualizarRig(
                headPos,
                headRot,
                leftPos,
                leftRot,
                rightPos,
                rightRot
            );
        }
    }

    public override void Render()
    {
        // Mi propio avatar está oculto para mí.
        // Solo renderizamos visuales de otros jugadores.
        if (Object.HasInputAuthority)
            return;

        float lerpSpeed = Time.deltaTime * 15f;

        if (_networkHeadVisual != null)
        {
            _networkHeadVisual.position = Vector3.Lerp(
                _networkHeadVisual.position,
                HeadPosition,
                lerpSpeed
            );

            _networkHeadVisual.rotation = Quaternion.Slerp(
                _networkHeadVisual.rotation,
                HeadRotation,
                lerpSpeed
            );
        }

        if (_networkLeftHandVisual != null)
        {
            _networkLeftHandVisual.position = Vector3.Lerp(
                _networkLeftHandVisual.position,
                LeftHandPosition,
                lerpSpeed
            );

            _networkLeftHandVisual.rotation = Quaternion.Slerp(
                _networkLeftHandVisual.rotation,
                LeftHandRotation,
                lerpSpeed
            );
        }

        if (_networkRightHandVisual != null)
        {
            _networkRightHandVisual.position = Vector3.Lerp(
                _networkRightHandVisual.position,
                RightHandPosition,
                lerpSpeed
            );

            _networkRightHandVisual.rotation = Quaternion.Slerp(
                _networkRightHandVisual.rotation,
                RightHandRotation,
                lerpSpeed
            );
        }
    }

    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    public void RPC_ActualizarRig(
        Vector3 headPos,
        Quaternion headRot,
        Vector3 leftPos,
        Quaternion leftRot,
        Vector3 rightPos,
        Quaternion rightRot
    )
    {
        ActualizarDatosDeRed(
            headPos,
            headRot,
            leftPos,
            leftRot,
            rightPos,
            rightRot
        );
    }

    private void ActualizarDatosDeRed(
        Vector3 headPos,
        Quaternion headRot,
        Vector3 leftPos,
        Quaternion leftRot,
        Vector3 rightPos,
        Quaternion rightRot
    )
    {
        HeadPosition = headPos;
        HeadRotation = headRot;

        LeftHandPosition = leftPos;
        LeftHandRotation = leftRot;

        RightHandPosition = rightPos;
        RightHandRotation = rightRot;
    }

    private void BuscarReferenciasLocales()
    {
        GameObject mainCamera = GameObject.FindGameObjectWithTag("MainCamera");

        if (mainCamera != null)
            _localHead = mainCamera.transform;
        else
            Debug.LogWarning("NetworkVRRig: No se encontró MainCamera.");

        GameObject leftHand = GameObject.Find("Left Controller");

        if (leftHand == null)
            leftHand = GameObject.Find("LeftHand Controller");

        if (leftHand != null)
            _localLeftHand = leftHand.transform;
        else
            Debug.LogWarning("NetworkVRRig: No se encontró Left Controller.");

        GameObject rightHand = GameObject.Find("Right Controller");

        if (rightHand == null)
            rightHand = GameObject.Find("RightHand Controller");

        if (rightHand != null)
            _localRightHand = rightHand.transform;
        else
            Debug.LogWarning("NetworkVRRig: No se encontró Right Controller.");
    }

    private void OcultarMisVisualesLocales()
    {
        Renderer[] renderers = GetComponentsInChildren<Renderer>();

        foreach (Renderer rend in renderers)
        {
            rend.enabled = false;
        }
    }

    private void TeletransportarRigLocalASpawn()
    {
        Transform playerObject = GameplayNetworkPlayerSpawner.BuscarObjetoJugadorLocal();

        if (playerObject == null)
        {
            Debug.LogWarning("NetworkVRRig: No se encontró el objeto 'player1' / local player para teletransportar.");
            return;
        }

        GameplayNetworkPlayerSpawner.TeletransportarTransform(playerObject, transform.position, transform.rotation);

        Debug.Log($"NetworkVRRig: Objeto local '{playerObject.name}' teletransportado a la posición de spawn: {transform.position}");
    }
}