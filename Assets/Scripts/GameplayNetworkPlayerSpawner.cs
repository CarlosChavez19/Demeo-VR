using UnityEngine;
using Fusion;
using System.Collections;

public class GameplayNetworkPlayerSpawner : MonoBehaviour
{
    [Header("Prefab del jugador")]
    [SerializeField] private NetworkPrefabRef playerPrefab;

    [Header("Spawns en Test")]
    [SerializeField] private Transform spawn1;
    [SerializeField] private Transform spawn2;
    [SerializeField] private Transform spawn3;

    [Header("Representación del Jugador Local (opcional)")]
    [SerializeField] private Transform player1;

    private bool yaSpawneo = false;

    private IEnumerator Start()
    {
        // Esperamos un poco para que Fusion termine de cargar la escena Test
        yield return new WaitForSeconds(1f);

        NetworkRunner runner = BuscarRunnerActivo();

        if (runner == null)
        {
            Debug.Log("GAMEPLAY SPAWNER: No se encontró NetworkRunner procedente de la lobby. Modo offline/Editor detectado.");
            TeletransportarCamaraLocalOffline();
            yield break;
        }

        runner.ProvideInput = true;

        Debug.Log("GAMEPLAY SPAWNER: Runner encontrado.");
        Debug.Log("GAMEPLAY SPAWNER: Soy host/server = " + runner.IsServer);

        int totalJugadores = 0;

        foreach (PlayerRef player in runner.ActivePlayers)
        {
            totalJugadores++;
            Debug.Log("GAMEPLAY SPAWNER: Player conectado = " + player);
        }

        Debug.Log("GAMEPLAY SPAWNER: Total jugadores conectados = " + totalJugadores);

        // Reposicionar la representación local del jugador (player1) de inmediato en su spawn
        TeletransportarJugadorLocal(runner);

        // Solo el host debe hacer los spawns de los objetos de red
        if (!runner.IsServer)
        {
            Debug.Log("GAMEPLAY SPAWNER: Soy cliente. El host hará los spawns.");
            yield break;
        }

        if (yaSpawneo)
            yield break;

        yaSpawneo = true;

        int index = 0;

        foreach (PlayerRef player in runner.ActivePlayers)
        {
            Transform spawn = ObtenerSpawnParaPlayer(player, index);

            Vector3 posicion = spawn != null ? spawn.position : Vector3.zero;
            Quaternion rotacion = spawn != null ? spawn.rotation : Quaternion.identity;

            runner.Spawn(
                playerPrefab,
                posicion,
                rotacion,
                player
            );

            Debug.Log($"GAMEPLAY SPAWNER: Network Player spawneado para {player} en posición {(spawn != null ? spawn.name : "Vector3.zero")}");

            index++;
        }
    }

    private void TeletransportarJugadorLocal(NetworkRunner runner)
    {
        if (runner == null) return;

        PlayerRef localPlayer = runner.LocalPlayer;
        Transform localSpawn = ObtenerSpawnParaPlayer(localPlayer, 0);

        Transform localObj = BuscarObjetoJugadorLocal(player1);

        if (localObj != null && localSpawn != null)
        {
            TeletransportarTransform(localObj, localSpawn.position, localSpawn.rotation);
            Debug.Log($"GAMEPLAY SPAWNER: Objeto de jugador local '{localObj.name}' movido a {localSpawn.name}.");
        }
    }

    private NetworkRunner BuscarRunnerActivo()
    {
        NetworkRunner[] runners =
            FindObjectsByType<NetworkRunner>(FindObjectsSortMode.None);

        foreach (NetworkRunner runner in runners)
        {
            if (runner == null)
                continue;

            int cantidad = 0;

            foreach (PlayerRef player in runner.ActivePlayers)
            {
                cantidad++;
            }

            if (cantidad > 0)
                return runner;
        }

        return null;
    }

    private Transform ObtenerSpawnParaPlayer(PlayerRef player, int index)
    {
        // Intentar buscar el rol del jugador en GameplayRoleCache
        foreach (GameplayRoleCache.PlayerInfo info in GameplayRoleCache.Players)
        {
            if (info.PlayerRef == player)
            {
                string rol = info.PlayerRole != null ? info.PlayerRole.Trim() : "";

                if (rol.Equals("Heroe 1", System.StringComparison.OrdinalIgnoreCase))
                    return spawn1 != null ? spawn1 : ObtenerSpawnPorIndice(index);

                if (rol.Equals("Heroe 2", System.StringComparison.OrdinalIgnoreCase))
                    return spawn2 != null ? spawn2 : ObtenerSpawnPorIndice(index);

                if (rol.Contains("Dungeon Master") || rol.Equals("DM", System.StringComparison.OrdinalIgnoreCase))
                    return spawn3 != null ? spawn3 : ObtenerSpawnPorIndice(index);
            }
        }

        // Si no se encontró rol en cache, usar el índice por defecto
        return ObtenerSpawnPorIndice(index);
    }

    private Transform ObtenerSpawnPorIndice(int index)
    {
        if (index == 0)
            return spawn1;

        if (index == 1)
            return spawn2;

        if (index == 2)
            return spawn3;

        return spawn1;
    }

    private void TeletransportarCamaraLocalOffline()
    {
        Transform spawnTarget = spawn1;
        if (spawnTarget == null) return;

        Transform localObj = BuscarObjetoJugadorLocal(player1);

        if (localObj != null)
        {
            TeletransportarTransform(localObj, spawnTarget.position, spawnTarget.rotation);
            Debug.Log("GAMEPLAY SPAWNER: Objeto local teletransportado a spawn1 en modo offline.");
        }
    }

    public static Transform BuscarObjetoJugadorLocal(Transform asignadoEnInspector = null)
    {
        if (asignadoEnInspector != null)
            return asignadoEnInspector;

        string[] nombresPosibles = new string[] { "player1", "Player1", "player", "Player", "XR Rig", "XROrigin", "[XR Rig]", "XR Interaction Setup" };

        foreach (string nombre in nombresPosibles)
        {
            GameObject obj = GameObject.Find(nombre);
            if (obj != null)
            {
                return obj.transform;
            }
        }

        GameObject playerByTag = GameObject.FindGameObjectWithTag("Player");
        if (playerByTag != null)
        {
            return playerByTag.transform;
        }

        GameObject mainCam = GameObject.FindGameObjectWithTag("MainCamera");
        if (mainCam != null)
        {
            Transform root = mainCam.transform;
            while (root.parent != null && root.parent.GetComponent<Canvas>() == null)
            {
                root = root.parent;
            }
            return root;
        }

        return null;
    }

    public static void TeletransportarTransform(Transform target, Vector3 posicionDeseada, Quaternion rotacionDeseada)
    {
        if (target == null) return;

        CharacterController cc = target.GetComponent<CharacterController>();
        if (cc != null) cc.enabled = false;

        Rigidbody rb = target.GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }

        GameObject mainCam = GameObject.FindGameObjectWithTag("MainCamera");
        if (mainCam != null && mainCam.transform.IsChildOf(target))
        {
            Vector3 offsetCam = mainCam.transform.position - target.position;
            target.position = posicionDeseada - new Vector3(offsetCam.x, 0, offsetCam.z);
        }
        else
        {
            target.position = posicionDeseada;
        }

        target.rotation = rotacionDeseada;

        if (cc != null) cc.enabled = true;
    }
}
