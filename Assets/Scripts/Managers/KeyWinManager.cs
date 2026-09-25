using System.Collections.Generic;
using UnityEngine;
using Fusion;
using DemeoVR.Gameplay;

public class KeyWinManager : NetworkBehaviour
{
    public static KeyWinManager Instance;

    [Header("Objeto visual de la llave")]
    [SerializeField] private GameObject keyObject;

    [Header("Configuración")]
    [SerializeField] private float alturaLlave = 0.5f;
    [SerializeField] private float intervaloRevision = 0.25f;
    [SerializeField] private int distanciaMinimaAlSpawn = 6;

    [Networked] public int KeyX { get; set; }
    [Networked] public int KeyZ { get; set; }
    [Networked] public bool KeyPlaced { get; set; }

    private bool networkReady = false;
    private bool victoriaProcesada = false;
    private float timerRevision = 0f;

    private void Awake()
    {
        Instance = this;
    }

    public override void Spawned()
    {
        networkReady = true;

        if (keyObject != null)
            keyObject.SetActive(true);

        if (Object.HasStateAuthority)
        {
            KeyPlaced = false;
            victoriaProcesada = false;

            GenerarLlaveAleatoria();
        }

        Debug.Log(
            "[KeyWinManager] Spawned. StateAuthority = " +
            Object.HasStateAuthority
        );
    }

    private void Update()
    {
        if (!networkReady)
            return;

        if (!Object.HasStateAuthority)
            return;

        if (!KeyPlaced)
            return;

        if (victoriaProcesada)
            return;

        if (GameEndManager.Instance != null &&
            GameEndManager.Instance.JuegoTerminado)
            return;

        timerRevision += Time.deltaTime;

        if (timerRevision < intervaloRevision)
            return;

        timerRevision = 0f;

        RevisarSiHeroeTocoLlave();
    }

    private void GenerarLlaveAleatoria()
    {
        CasillaComponent[] casillas =
            FindObjectsByType<CasillaComponent>(FindObjectsSortMode.None);

        List<CasillaComponent> casillasValidas =
            new List<CasillaComponent>();

        foreach (CasillaComponent casilla in casillas)
        {
            if (casilla == null)
                continue;

            if (casilla.esObstaculo)
                continue;

            if (casilla.estaOcupada)
                continue;

            casillasValidas.Add(casilla);
        }

        if (casillasValidas.Count == 0)
        {
            Debug.LogError("[KeyWinManager HOST] No hay casillas válidas para colocar la llave.");
            return;
        }

        // Filtramos para evitar que la llave aparezca muy cerca del spawn de héroes
        List<CasillaComponent> casillasFiltradas = new List<CasillaComponent>();
        List<CasillaComponent> casillasSpawn = new List<CasillaComponent>();

        foreach (CasillaComponent casilla in casillas)
        {
            if (casilla != null && casilla.esSpawnHeroe)
            {
                casillasSpawn.Add(casilla);
            }
        }

        if (casillasSpawn.Count > 0)
        {
            foreach (CasillaComponent casilla in casillasValidas)
            {
                bool muyCerca = false;
                foreach (CasillaComponent spawn in casillasSpawn)
                {
                    int dx = Mathf.Abs(casilla.coordenadaX - spawn.coordenadaX);
                    int dz = Mathf.Abs(casilla.coordenadaZ - spawn.coordenadaZ);
                    int distGrid = Mathf.Max(dx, dz); // Distancia Chebyshev (pasos en tablero 8-direcciones)

                    if (distGrid < distanciaMinimaAlSpawn)
                    {
                        muyCerca = true;
                        break;
                    }
                }

                if (!muyCerca)
                {
                    casillasFiltradas.Add(casilla);
                }
            }
        }
        else
        {
            casillasFiltradas = casillasValidas;
        }

        // Si no quedan casillas tras aplicar el filtro, ignoramos la restricción
        if (casillasFiltradas.Count == 0)
        {
            Debug.LogWarning(
                $"[KeyWinManager HOST] El filtro de distancia mínima ({distanciaMinimaAlSpawn}) " +
                "dejó 0 casillas válidas. Usando todas las casillas disponibles."
            );
            casillasFiltradas = casillasValidas;
        }

        int index = Random.Range(0, casillasFiltradas.Count);
        CasillaComponent casillaElegida = casillasFiltradas[index];

        KeyX = casillaElegida.coordenadaX;
        KeyZ = casillaElegida.coordenadaZ;
        KeyPlaced = true;

        Vector3 posicionLlave =
            casillaElegida.ObtenerCentro() + Vector3.up * alturaLlave;

        RPC_MostrarLlave(posicionLlave);

        Debug.Log(
            "[KeyWinManager HOST] Llave generada automáticamente. " +
            "X = " + KeyX +
            " | Z = " + KeyZ +
            " | Posición = " + posicionLlave
        );
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_MostrarLlave(Vector3 posicion)
    {
        if (keyObject != null)
        {
            keyObject.transform.position = posicion;
        }

        Debug.Log(
            "[KeyWinManager TODOS] Llave posicinada en = " +
            posicion
        );

        if (GridManager.Instance != null)
        {
            GridManager.Instance.ActualizarNieblaDeGuerraGlobal();
        }
    }

    private void RevisarSiHeroeTocoLlave()
    {
        FichaRPG[] fichas =
            FindObjectsByType<FichaRPG>(FindObjectsSortMode.None);

        foreach (FichaRPG ficha in fichas)
        {
            if (ficha == null)
                continue;

            if (!ficha.esHeroe)
                continue;

            if (!EsHeroeActivo(ficha.RolPropietario))
                continue;

            if (ficha.casillaActual == null)
                continue;

            BoardPiece statsHeroe = ObtenerBoardPiece(ficha);

            if (statsHeroe != null && statsHeroe.CurrentHealth <= 0)
                continue;

            bool llegoALaLlave =
                ficha.casillaActual.coordenadaX == KeyX &&
                ficha.casillaActual.coordenadaZ == KeyZ;

            if (llegoALaLlave)
            {
                victoriaProcesada = true;

                Debug.Log(
                    "[KeyWinManager HOST] Héroe llegó a la llave. " +
                    "Ficha = " + ficha.name +
                    " | Rol = " + ficha.RolPropietario
                );

                RPC_OcultarLlave();

                if (GameEndManager.Instance != null)
                {
                    GameEndManager.Instance.TerminarJuegoHeroesGanan();
                }
                else
                {
                    Debug.LogWarning("[KeyWinManager HOST] No existe GameEndManager.");
                }

                return;
            }
        }
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_OcultarLlave()
    {
        if (keyObject != null)
            keyObject.SetActive(false);

        Debug.Log("[KeyWinManager TODOS] Llave ocultada por victoria de héroes.");
    }

    public void ActualizarVisibilidadNiebla(HashSet<CasillaComponent> casillasVisibles)
    {
        if (keyObject == null)
            return;

        if (!networkReady || Object == null || !Object.IsValid)
        {
            SetRenderersEnabled(false);
            return;
        }
        bool estaVisible = false;

        string rolLocal = GameplayRoleCache.LocalRole;

        if (rolLocal == "Dungeon Master")
        {
            // El Dungeon Master SIEMPRE ve la llave para poder defenderla
            estaVisible = true;
        }
        else
        {
            // Los héroes solo la ven si la casilla está fuera de la niebla de guerra
            if (GridManager.Instance != null &&
                GridManager.Instance.DiccionarioTablero.TryGetValue(new Vector2Int(KeyX, KeyZ), out CasillaComponent casillaLlave))
            {
                estaVisible = casillasVisibles != null && casillasVisibles.Contains(casillaLlave);
            }
        }

        SetRenderersEnabled(estaVisible);

        Debug.Log($"[KeyWinManager] Visibilidad render de la llave actualizada: Visible = {estaVisible} (Rol = {rolLocal}) en Casilla ({KeyX}, {KeyZ})");
    }

    private void SetRenderersEnabled(bool state)
    {
        if (keyObject == null) return;

        Renderer[] renderers = keyObject.GetComponentsInChildren<Renderer>(true);
        foreach (Renderer r in renderers)
        {
            r.enabled = state;
        }
    }

    private bool EsHeroeActivo(string rol)
    {
        if (GameplayManager.Instance == null)
            return false;

        if (GameplayManager.Instance.TurnOrder == null)
            return false;

        foreach (GameplayRoleCache.PlayerInfo info in GameplayManager.Instance.TurnOrder)
        {
            if (info.PlayerRole == rol)
                return true;
        }

        return false;
    }

    private BoardPiece ObtenerBoardPiece(Component component)
    {
        if (component == null)
            return null;

        BoardPiece pieza = component.GetComponent<BoardPiece>();

        if (pieza == null)
            pieza = component.GetComponentInParent<BoardPiece>();

        if (pieza == null)
            pieza = component.GetComponentInChildren<BoardPiece>();

        return pieza;
    }
}