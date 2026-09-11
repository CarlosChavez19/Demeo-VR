using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Fusion;
using DemeoVR.Gameplay;

public class GameEndManager : NetworkBehaviour
{
    public static GameEndManager Instance;

    [Header("UI Game Over")]
    [SerializeField] private GameObject panelGameOver;
    [SerializeField] private Image imagenFondoGameOver;
    [SerializeField] private TMP_Text textoGameOver;
    [SerializeField] private Button botonVolverLobby;

    [Header("Fondos Pantalla Final - Sprites (4 Fondos)")]
    [Tooltip("Fondo de victoria para Héroes (Heroe 1 y Heroe 2)")]
    [SerializeField] private Sprite fondoVictoriaHeroes;
    [Tooltip("Fondo de derrota para Héroes (Heroe 1 y Heroe 2)")]
    [SerializeField] private Sprite fondoDerrotaHeroes;
    [Tooltip("Fondo de victoria para el Dungeon Master")]
    [SerializeField] private Sprite fondoVictoriaDM;
    [Tooltip("Fondo de derrota para el Dungeon Master")]
    [SerializeField] private Sprite fondoDerrotaDM;

    [Header("Configuración")]
    [SerializeField] private float intervaloRevision = 0.5f;

    private float timerRevision = 0f;
    private bool juegoTerminado = false;

    private HashSet<string> heroesYaEliminados = new HashSet<string>();

    public bool JuegoTerminado => juegoTerminado;

    private void Awake()
    {
        Instance = this;
    }

    public override void Spawned()
    {
        OcultarPanelGameOver();

        Debug.Log(
            "[GameEndManager] Spawned. " +
            "StateAuthority = " + Object.HasStateAuthority +
            " | LocalPlayer = " + Runner.LocalPlayer
        );
    }

    private void Update()
    {
        if (juegoTerminado)
            return;

        if (!Object.HasStateAuthority)
            return;

        timerRevision += Time.deltaTime;

        if (timerRevision < intervaloRevision)
            return;

        timerRevision = 0f;

        RevisarDerrotaHeroes();
    }

    private void RevisarDerrotaHeroes()
    {
        if (GameplayManager.Instance == null)
        {
            Debug.LogWarning("[GameEndManager HOST] No existe GameplayManager.");
            return;
        }

        List<string> rolesHeroesActivos = ObtenerRolesHeroesActivos();

        if (rolesHeroesActivos.Count == 0)
        {
            Debug.LogWarning("[GameEndManager HOST] No hay héroes activos.");
            return;
        }

        int heroesMuertos = 0;

        foreach (string rolHeroe in rolesHeroesActivos)
        {
            FichaRPG fichaHeroe = BuscarFichaHeroePorRol(rolHeroe);

            if (fichaHeroe == null)
            {
                Debug.LogWarning(
                    "[GameEndManager HOST] No se encontró ficha para " + rolHeroe
                );

                continue;
            }

            BoardPiece statsHeroe = ObtenerBoardPiece(fichaHeroe);

            if (statsHeroe == null)
            {
                Debug.LogWarning(
                    "[GameEndManager HOST] La ficha " +
                    fichaHeroe.name +
                    " no tiene BoardPiece."
                );

                continue;
            }

            Debug.Log(
                "[GameEndManager HOST] Revisando héroe. " +
                "Rol = " + rolHeroe +
                " | HP = " + statsHeroe.CurrentHealth
            );

            if (statsHeroe.CurrentHealth <= 0)
            {
                heroesMuertos++;

                if (!heroesYaEliminados.Contains(rolHeroe))
                {
                    heroesYaEliminados.Add(rolHeroe);

                    Debug.Log(
                        "[GameEndManager HOST] Eliminando ficha caída. Rol = " +
                        rolHeroe
                    );

                    RPC_EliminarHeroeCaido(rolHeroe);
                }
            }
        }

        bool todosLosHeroesActivosMurieron =
            heroesMuertos >= rolesHeroesActivos.Count;

        if (todosLosHeroesActivosMurieron)
        {
            juegoTerminado = true;

            Debug.Log(
                "[GameEndManager HOST] FIN DEL JUEGO. Gana el Dungeon Master. " +
                "Héroes muertos = " + heroesMuertos +
                " / Héroes activos = " + rolesHeroesActivos.Count
            );

            RPC_MostrarGameOver("DM_WIN");
        }
    }

    private List<string> ObtenerRolesHeroesActivos()
    {
        List<string> roles = new List<string>();

        if (GameplayManager.Instance == null)
            return roles;

        if (GameplayManager.Instance.TurnOrder == null)
            return roles;

        foreach (GameplayRoleCache.PlayerInfo info in GameplayManager.Instance.TurnOrder)
        {
            if (info.PlayerRole == "Heroe 1" || info.PlayerRole == "Heroe 2")
            {
                if (!roles.Contains(info.PlayerRole))
                {
                    roles.Add(info.PlayerRole);
                }
            }
        }

        return roles;
    }

    private FichaRPG BuscarFichaHeroePorRol(string rol)
    {
        FichaRPG[] fichas =
            FindObjectsByType<FichaRPG>(FindObjectsSortMode.None);

        foreach (FichaRPG ficha in fichas)
        {
            if (!ficha.esHeroe)
                continue;

            if (ficha.RolPropietario == rol)
                return ficha;
        }

        return null;
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

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_EliminarHeroeCaido(string rolHeroe)
    {
        FichaRPG fichaHeroe = BuscarFichaHeroePorRol(rolHeroe);

        if (fichaHeroe == null)
        {
            Debug.LogWarning(
                "[GameEndManager TODOS] No se pudo eliminar ficha. No existe rol = " +
                rolHeroe
            );

            return;
        }

        if (fichaHeroe.casillaActual != null)
        {
            fichaHeroe.casillaActual.estaOcupada = false;
            fichaHeroe.casillaActual = null;
        }

        Renderer[] renderers = fichaHeroe.GetComponentsInChildren<Renderer>(true);

        foreach (Renderer renderer in renderers)
        {
            renderer.enabled = false;
        }

        Collider[] colliders = fichaHeroe.GetComponentsInChildren<Collider>(true);

        foreach (Collider collider in colliders)
        {
            collider.enabled = false;
        }

        Rigidbody rb = fichaHeroe.GetComponent<Rigidbody>();

        if (rb != null)
        {
            rb.isKinematic = true;
        }

        fichaHeroe.enabled = false;

        Debug.Log(
            "[GameEndManager TODOS] Ficha de héroe eliminada visualmente. Rol = " +
            rolHeroe
        );
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_MostrarGameOver(string resultado)
    {
        juegoTerminado = true;

        string rolLocal = "Desconocido";

        if (GameplayManager.Instance != null)
            rolLocal = GameplayManager.Instance.LocalPlayerRole;

        string mensajeFinal = "";

        if (resultado == "DM_WIN")
        {
            if (rolLocal == "Dungeon Master")
            {
                mensajeFinal = "Juego terminado\n¡Ganaste!";
            }
            else
            {
                mensajeFinal = "Juego terminado\nPerdiste";
            }
        }
        else if (resultado == "HEROES_WIN")
        {
            if (rolLocal == "Dungeon Master")
            {
                mensajeFinal = "Juego terminado\nPerdiste";
            }
            else
            {
                mensajeFinal = "Juego terminado\n¡Ganaste!";
            }
        }
        else
        {
            mensajeFinal = "Juego terminado";
        }

        if (panelGameOver != null)
            panelGameOver.SetActive(true);

        ActualizarFondoGameOver(resultado, rolLocal);

        if (textoGameOver != null)
            textoGameOver.text = mensajeFinal;

        if (botonVolverLobby != null)
            botonVolverLobby.interactable = true;

        GameplayUIManager uiManager = FindFirstObjectByType<GameplayUIManager>();

        if (uiManager != null)
        {
            uiManager.BloquearUIFinJuego();
        }

        HeroCardUI cardUI =
            FindFirstObjectByType<HeroCardUI>();

        if (cardUI != null)
        {
            cardUI.BloquearCartasFinJuego();
        }

        Debug.Log(
            "[GameEndManager TODOS] GAME OVER mostrado. " +
            "Resultado = " + resultado +
            " | Rol local = " + rolLocal +
            " | Mensaje = " + mensajeFinal
        );
    }
    public void TerminarJuegoHeroesGanan()
    {
        if (!Object.HasStateAuthority)
            return;

        if (juegoTerminado)
            return;

        juegoTerminado = true;

        Debug.Log("[GameEndManager HOST] FIN DEL JUEGO. Ganan los héroes.");

        RPC_MostrarGameOver("HEROES_WIN");
    }
    private void OcultarPanelGameOver()
    {
        if (panelGameOver != null)
            panelGameOver.SetActive(false);

        if (botonVolverLobby != null)
            botonVolverLobby.interactable = false;
    }

    private void ActualizarFondoGameOver(string resultado, string rolLocal)
    {
        bool esDM = (rolLocal == "Dungeon Master");
        bool esVictoria = false;

        if (resultado == "DM_WIN")
        {
            esVictoria = esDM;
        }
        else if (resultado == "HEROES_WIN")
        {
            esVictoria = !esDM;
        }

        Sprite spriteSeleccionado = null;

        if (esDM)
        {
            spriteSeleccionado = esVictoria ? fondoVictoriaDM : fondoDerrotaDM;
        }
        else
        {
            // Aplica tanto a Héroe 1 como a Héroe 2
            spriteSeleccionado = esVictoria ? fondoVictoriaHeroes : fondoDerrotaHeroes;
        }

        if (imagenFondoGameOver != null && spriteSeleccionado != null)
        {
            imagenFondoGameOver.sprite = spriteSeleccionado;
        }

        Debug.Log(
            $"[GameEndManager TODOS] Fondo actualizado en cliente local. Rol = {rolLocal} | EsDM = {esDM} | EsVictoria = {esVictoria}"
        );
    }
}