using System.Collections;
using System.Threading.Tasks;
using UnityEngine;
using DemeoVR.Gameplay;
using System.Runtime.CompilerServices;

public class FichaRPG : MonoBehaviour
{
    [Header("Estadísticas (ScriptableObject)")]
    public PieceData estadisticasBase;

    [Header("Posicionamiento y Memoria")]
    public CasillaComponent casillaActual;
    [SerializeField] private CasillaComponent casillaPrevisualizada;

    [Header("Interacción y Física VR")]
    [SerializeField] private bool estaSiendoSostenida;
    [SerializeField] private LayerMask capaTablero;

    [Header("Pruebas Locales")]
    [SerializeField] private bool ignorarValidacionMultijugador = false;

    [Header("Restricciones")]
    public int rangoMovimiento = 3;
    [SerializeField] private bool haMovidoEsteTurno = false;
    public bool HaMovidoEsteTurno => haMovidoEsteTurno;

    private int ultimoTurnoDetectadoFicha = -1;

    [Header("Propiedad y Turnos")]
    public bool esHeroe;

    [Tooltip("Valores válidos: 'Heroe 1', 'Heroe 2', 'Dungeon Master'")]
    [SerializeField] private string rolPropietario;

    private Rigidbody rb;
    private GridManager gridManager;

    // Lo usamos como Behaviour para evitar problemas de namespace entre versiones de XR Interaction Toolkit
    private Behaviour grabInteractable;

    private bool permisoAgarreInicializado = false;
    private bool ultimoPermisoAgarre = true;

    [Header("Ajuste de Imán")]
    [SerializeField] private float alturaSobreCasilla = 0.6f;
    public string RolPropietario => rolPropietario;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        gridManager = FindFirstObjectByType<GridManager>();

        grabInteractable = BuscarXRGrabInteractable();

        if (grabInteractable == null)
        {
            Debug.LogWarning(
                "[FichaRPG] No se encontró XRGrabInteractable en " + gameObject.name +
                ". Si esta ficha debe agarrarse en VR, revisa que el componente esté en el mismo GameObject."
            );
        }
    }

    private IEnumerator Start()
    {
        // Esperamos un frame para que GridManager pueda inicializar spawns/casillas
        yield return null;

        if (gridManager == null)
            gridManager = GridManager.Instance != null ? GridManager.Instance : FindFirstObjectByType<GridManager>();

        // Si por alguna razón no se asignó la casilla al venir desde Lobby -> Test,
        // intentamos detectarla debajo de la ficha.
        if (casillaActual == null)
        {
            IntentarDetectarCasillaActualPorRaycast();
        }

        ActualizarPermisoDeAgarre(true);

        Debug.Log(
            "[FichaRPG] Inicializada: " + gameObject.name +
            " | Rol propietario = " + rolPropietario +
            " | EsHeroe = " + esHeroe +
            " | CasillaActual = " + (casillaActual != null ? casillaActual.name : "NULL") +
            " | LocalRole = " + ObtenerRolLocalSeguro()
        );
    }

    private void Update()
    {
        ActualizarPermisoDeAgarre(false);

        // Si por algún bug la ficha quedó sostenida cuando ya no debería,
        // la devolvemos a su casilla.
        if (estaSiendoSostenida && !PuedeMoverEstaFicha(false))
        {
            Debug.LogWarning(
                "[FichaRPG] La ficha estaba siendo sostenida sin permiso. Se cancela movimiento. Ficha = " +
                gameObject.name
            );

            CancelarMovimientoLocal();
            return;
        }

        if (estaSiendoSostenida)
        {
            if (Physics.Raycast(transform.position, Vector3.down, out RaycastHit hit, Mathf.Infinity, capaTablero))
            {
                CasillaComponent casillaDetectada = hit.collider.GetComponent<CasillaComponent>();

                bool permitida = false;

                if (gridManager != null && casillaDetectada != null)
                {
                    permitida = gridManager.EsCasillaValida(casillaDetectada);
                }

                if (casillaDetectada != null &&
                    permitida &&
                    (!casillaDetectada.estaOcupada || casillaDetectada == casillaActual))
                {
                    if (casillaPrevisualizada != null && casillaPrevisualizada != casillaDetectada)
                    {
                        casillaPrevisualizada.SetearEstadoVisual("EnRango");
                    }

                    casillaPrevisualizada = casillaDetectada;
                    casillaPrevisualizada.SetearEstadoVisual("Hover");
                }
                else
                {
                    if (casillaPrevisualizada != null)
                    {
                        casillaPrevisualizada.SetearEstadoVisual("EnRango");
                        casillaPrevisualizada = null;
                    }
                }
            }
            else
            {
                if (casillaPrevisualizada != null)
                {
                    casillaPrevisualizada.SetearEstadoVisual("EnRango");
                    casillaPrevisualizada = null;
                }
            }
        }
    }

    [ContextMenu("Levantar Ficha (Simular)")]
    public void AlSerLevantada()
    {
        if (!PuedeMoverEstaFicha(true))
        {
            CancelarMovimientoLocal();
            return;
        }

        if (casillaActual == null)
        {
            Debug.LogWarning(
                "[FichaRPG] casillaActual era NULL al levantar. Intentando detectar casilla por raycast. Ficha = " +
                gameObject.name
            );

            IntentarDetectarCasillaActualPorRaycast();
        }

        if (casillaActual == null)
        {
            Debug.LogError(
                "[FichaRPG] No se puede levantar la ficha porque casillaActual sigue siendo NULL. Ficha = " +
                gameObject.name
            );

            CancelarMovimientoLocal();
            return;
        }

        if (rb != null)
        {
            rb.isKinematic = false;
        }

        estaSiendoSostenida = true;

        Debug.Log(
            "[FichaRPG] Ficha levantada correctamente. " +
            "Ficha = " + gameObject.name +
            " | Rol propietario = " + rolPropietario +
            " | Rol local = " + ObtenerRolLocalSeguro() +
            " | Casilla actual = " + casillaActual.name +
            " | Rango movimiento = " + rangoMovimiento
        );

        if (GridManager.Instance != null && casillaActual != null)
        {
            GridManager.Instance.MostrarRangoMovimiento(casillaActual, rangoMovimiento);
        }
    }

    [ContextMenu("Soltar Ficha (Simular)")]
    public void AlSerSoltada()
    {
        if (!estaSiendoSostenida)
        {
            Debug.LogWarning(
                "[FichaRPG] Se llamó AlSerSoltada, pero la ficha no estaba sostenida/autorizada. Ficha = " +
                gameObject.name
            );

            CancelarMovimientoLocal();
            return;
        }

        estaSiendoSostenida = false;

        bool casillaEnRango = false;

        if (gridManager != null && casillaPrevisualizada != null)
        {
            casillaEnRango = gridManager.EsCasillaValida(casillaPrevisualizada);
        }

        if (casillaPrevisualizada != null &&
            casillaEnRango &&
            !casillaPrevisualizada.estaOcupada &&
            casillaPrevisualizada != casillaActual)
        {
            Debug.Log(
                "[FichaRPG] Movimiento válido. " +
                "Ficha = " + gameObject.name +
                " | Desde = " + casillaActual.name +
                " | Hacia = " + casillaPrevisualizada.name
            );

            ColocarEnCasilla(casillaPrevisualizada);
        }
        else
        {
            Debug.LogWarning(
                "[FichaRPG] Movimiento cancelado o inválido. La ficha vuelve a su casilla. " +
                "Ficha = " + gameObject.name +
                " | Casilla actual = " + (casillaActual != null ? casillaActual.name : "NULL") +
                " | Casilla preview = " + (casillaPrevisualizada != null ? casillaPrevisualizada.name : "NULL") +
                " | En rango = " + casillaEnRango
            );

            CancelarMovimientoLocal();
        }

        casillaPrevisualizada = null;

        if (GridManager.Instance != null)
        {
            GridManager.Instance.OcultarRangoMovimiento();
        }

        if (GridManager.Instance != null)
        {
            GridManager.Instance.ActualizarNieblaDeGuerraGlobal();
        }
    }

    public void ColocarEnCasilla(CasillaComponent nuevaCasilla)
    {
        if (nuevaCasilla == null)
        {
            RegresarACasillaActual();
            return;
        }

        if (CasillaEstaBloqueadaParaHeroe(nuevaCasilla))
        {
            Debug.Log(
                "[FichaRPG] No puedes colocar la ficha en esa casilla. " +
                "Está bloqueada u ocupada. X = " +
                nuevaCasilla.coordenadaX +
                " | Z = " +
                nuevaCasilla.coordenadaZ
            );

            RegresarACasillaActual();
            return;
        }

        bool esMovimientoNuevo = (casillaActual != null && casillaActual != nuevaCasilla);

        // 1. Aplicar colocación lógica y física de forma local
        ColocarEnCasillaDesdeRed(nuevaCasilla);

        if (esMovimientoNuevo)
        {
            haMovidoEsteTurno = true;
        }

        // 2. Sincronizar en red por medio de BoardPiece si aplica
        BoardPiece boardPiece = GetComponent<BoardPiece>();
        if (boardPiece != null && boardPiece.Object != null)
        {
            if (!boardPiece.Object.HasStateAuthority)
            {
                // El cliente solicita al host que replique la casilla
                boardPiece.RPC_RequestColocarEnCasilla(nuevaCasilla.coordenadaX, nuevaCasilla.coordenadaZ);
            }
            else
            {
                if (esMovimientoNuevo)
                {
                    boardPiece.HasMovedThisTurn = true;
                }
                // El host retransmite directamente a todos los clientes
                boardPiece.RPC_MudarFichaATodos(nuevaCasilla.coordenadaX, nuevaCasilla.coordenadaZ);
            }
        }
    }

    public void ColocarEnCasillaDesdeRed(CasillaComponent nuevaCasilla)
    {
        if (nuevaCasilla == null)
            return;

        if (casillaActual != null && casillaActual != nuevaCasilla)
        {
            casillaActual.estaOcupada = false;
        }

        casillaActual = nuevaCasilla;
        casillaActual.estaOcupada = true;

        IniciarReposicionamiento(nuevaCasilla);

        Rigidbody rb = GetComponent<Rigidbody>();

        if (rb != null)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.isKinematic = true;
        }

        Debug.Log(
            "[FichaRPG] Ficha colocada lógicamente en casilla: " +
            nuevaCasilla.coordenadaX + ", " +
            nuevaCasilla.coordenadaZ
        );
    }

    private Coroutine reposicionarCoroutine;

    private void IniciarReposicionamiento(CasillaComponent casilla)
    {
        if (reposicionarCoroutine != null)
        {
            StopCoroutine(reposicionarCoroutine);
        }
        reposicionarCoroutine = StartCoroutine(ReposicionarEnCasillaCoroutine(casilla));
    }

    private IEnumerator ReposicionarEnCasillaCoroutine(CasillaComponent casilla)
    {
        yield return new WaitForSeconds(0.2f);

        if (casilla != null)
        {
            transform.rotation = Quaternion.identity;
            transform.position = ObtenerPosicionFijaEnCasilla(casilla);

            Rigidbody rb = GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
                rb.isKinematic = true;
            }
        }
        reposicionarCoroutine = null;
    }

    public void ColocarEnCasillaInicial(CasillaComponent nuevaCasilla)
    {
        ColocarEnCasillaDesdeRed(nuevaCasilla);
        haMovidoEsteTurno = false;

        BoardPiece bp = GetComponent<BoardPiece>();
        if (bp == null) bp = GetComponentInParent<BoardPiece>();
        if (bp != null && bp.Object != null && bp.Object.HasStateAuthority)
        {
            bp.HasMovedThisTurn = false;
        }
    }

    public void ReiniciarTurno()
    {
        haMovidoEsteTurno = false;

        BoardPiece bp = GetComponent<BoardPiece>();
        if (bp == null) bp = GetComponentInParent<BoardPiece>();
        if (bp != null && bp.Object != null && bp.Object.HasStateAuthority)
        {
            bp.HasMovedThisTurn = false;
        }
    }

    private bool PuedeMoverEstaFicha(bool mostrarLogs)
    {
        if (ignorarValidacionMultijugador)
        {
            if (mostrarLogs)
            {
                Debug.LogWarning(
                    "[FichaRPG] Validación multijugador ignorada para pruebas locales. Ficha = " +
                    gameObject.name
                );
            }

            return true;
        }

        // Si estamos probando la escena Test directamente sin venir de lobby,
        // permitimos mover para que las pruebas locales sigan funcionando.
        if (GameplayManager.Instance == null && TurnManager.Instance == null)
        {
            if (mostrarLogs)
            {
                Debug.Log(
                    "[FichaRPG] Modo prueba local detectado. No hay GameplayManager ni TurnManager. Ficha = " +
                    gameObject.name
                );
            }

            return true;
        }

        if (GameplayManager.Instance == null)
        {
            if (mostrarLogs)
                Debug.LogWarning("[FichaRPG] No existe GameplayManager. Ficha = " + gameObject.name);

            return false;
        }

        if (TurnManager.Instance == null)
        {
            if (mostrarLogs)
                Debug.LogWarning("[FichaRPG] No existe TurnManager. Ficha = " + gameObject.name);

            return false;
        }

        string rolLocal = GameplayManager.Instance.LocalPlayerRole;

        if (string.IsNullOrEmpty(rolLocal) || rolLocal == "Sin rol")
        {
            if (mostrarLogs)
            {
                Debug.LogWarning(
                    "[FichaRPG] El rol local aún no está listo. " +
                    "Ficha = " + gameObject.name +
                    " | Rol local = " + rolLocal
                );
            }

            return false;
        }

        if (!TurnManager.Instance.IsMyTurn())
        {
            if (mostrarLogs)
            {
                Debug.LogWarning(
                    "[FichaRPG] Movimiento cancelado. No es tu turno. " +
                    "Ficha = " + gameObject.name +
                    " | Rol local = " + rolLocal +
                    " | Rol propietario = " + rolPropietario
                );
            }

            return false;
        }

        if (rolPropietario != rolLocal)
        {
            if (mostrarLogs)
            {
                Debug.LogWarning(
                    "[FichaRPG] Agarre cancelado. Esta ficha no te pertenece. " +
                    "Ficha = " + gameObject.name +
                    " | Rol propietario = " + rolPropietario +
                    " | Tu rol = " + rolLocal
                );
            }

            return false;
        }

        if (haMovidoEsteTurno)
        {
            if (mostrarLogs)
            {
                Debug.LogWarning(
                    "[FichaRPG] Movimiento cancelado. El héroe ya se movió en este turno. Ficha = " +
                    gameObject.name
                );
            }

            return false;
        }

        BoardPiece boardPiece = GetComponent<BoardPiece>();
        if (boardPiece == null)
            boardPiece = GetComponentInParent<BoardPiece>();

        if (boardPiece != null)
        {
            if (boardPiece.HasAttackedThisTurn)
            {
                if (mostrarLogs)
                {
                    Debug.LogWarning(
                        "[FichaRPG] Movimiento cancelado. El héroe ya realizó un ataque en este turno. Ficha = " +
                        gameObject.name
                    );
                }

                return false;
            }

            if (boardPiece.HasMovedThisTurn)
            {
                if (mostrarLogs)
                {
                    Debug.LogWarning(
                        "[FichaRPG] Movimiento cancelado. El héroe ya se movió en este turno (Red). Ficha = " +
                        gameObject.name
                    );
                }

                return false;
            }
        }

        return true;
    }

    private void ActualizarPermisoDeAgarre(bool forzarLog)
    {
        if (TurnManager.Instance != null)
        {
            int turnoActual = TurnManager.Instance.CurrentTurnIndex;
            if (turnoActual != ultimoTurnoDetectadoFicha)
            {
                ultimoTurnoDetectadoFicha = turnoActual;
                haMovidoEsteTurno = false;
            }
        }

        if (grabInteractable == null)
            return;

        bool permisoActual = PuedeMoverEstaFicha(false);

        if (!permisoAgarreInicializado || permisoActual != ultimoPermisoAgarre || forzarLog)
        {
            grabInteractable.enabled = permisoActual;

            Debug.Log(
                "[FichaRPG] Permiso de agarre actualizado. " +
                "Ficha = " + gameObject.name +
                " | Puede agarrar = " + permisoActual +
                " | Rol propietario = " + rolPropietario +
                " | Rol local = " + ObtenerRolLocalSeguro() +
                " | Es mi turno = " + ObtenerTurnoLocalSeguro()
            );

            ultimoPermisoAgarre = permisoActual;
            permisoAgarreInicializado = true;
        }
    }

    private void CancelarMovimientoLocal()
    {
        estaSiendoSostenida = false;
        casillaPrevisualizada = null;

        haMovidoEsteTurno = false;

        BoardPiece bp = GetComponent<BoardPiece>();
        if (bp == null) bp = GetComponentInParent<BoardPiece>();
        if (bp != null && bp.Object != null && bp.Object.HasStateAuthority)
        {
            bp.HasMovedThisTurn = false;
        }

        if (GridManager.Instance != null)
        {
            GridManager.Instance.OcultarRangoMovimiento();
        }

        if (rb != null)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.isKinematic = true;
        }

        if (casillaActual != null)
        {
            //transform.position = casillaActual.ObtenerCentro();
            //transform.rotation = Quaternion.identity;
            IniciarReposicionamiento(casillaActual);
            casillaActual.estaOcupada = true;

            SincronizarPosicionEnRed(casillaActual);
        }
    }

    private bool IntentarDetectarCasillaActualPorRaycast()
    {
        Vector3 origenRaycast = transform.position + Vector3.up * 1.5f;

        if (Physics.Raycast(origenRaycast, Vector3.down, out RaycastHit hit, 5f, capaTablero))
        {
            CasillaComponent casillaDetectada = hit.collider.GetComponentInParent<CasillaComponent>();

            if (casillaDetectada != null)
            {
                casillaActual = casillaDetectada;
                casillaActual.estaOcupada = true;

                transform.position = casillaActual.ObtenerCentro();
                transform.rotation = Quaternion.identity;

                if (rb != null)
                {
                    rb.linearVelocity = Vector3.zero;
                    rb.angularVelocity = Vector3.zero;
                    rb.isKinematic = true;
                }

                Debug.Log(
                    "[FichaRPG] Casilla actual detectada por raycast. " +
                    "Ficha = " + gameObject.name +
                    " | Casilla = " + casillaActual.name
                );

                return true;
            }
        }

        Debug.LogWarning(
            "[FichaRPG] No se pudo detectar casillaActual por raycast. " +
            "Ficha = " + gameObject.name +
            " | Revisa capaTablero y colliders de las casillas."
        );

        return false;
    }

    private Behaviour BuscarXRGrabInteractable()
    {
        MonoBehaviour[] componentes = GetComponents<MonoBehaviour>();

        foreach (MonoBehaviour componente in componentes)
        {
            if (componente == null)
                continue;

            if (componente.GetType().Name == "XRGrabInteractable")
                return componente;
        }

        return null;
    }

    private string ObtenerRolLocalSeguro()
    {
        if (GameplayManager.Instance == null)
            return "Sin GameplayManager";

        return GameplayManager.Instance.LocalPlayerRole;
    }

    private string ObtenerTurnoLocalSeguro()
    {
        if (TurnManager.Instance == null)
            return "Sin TurnManager";

        return TurnManager.Instance.IsMyTurn().ToString();
    }
    private Vector3 ObtenerPosicionFijaEnCasilla(CasillaComponent casilla)
    {
        Vector3 posicion = casilla.ObtenerCentro();
        posicion.y += alturaSobreCasilla;
        return posicion;
    }
    private bool CasillaEstaBloqueadaParaHeroe(CasillaComponent casillaDestino)
    {
        if (casillaDestino == null)
            return true;

        if (casillaDestino.esObstaculo)
            return true;

        // Si es la misma casilla donde ya estoy, no la bloqueo contra mí mismo
        if (casillaDestino == casillaActual)
            return false;

        // Esto representa otra ficha o enemigo ocupando esa casilla
        if (casillaDestino.estaOcupada)
            return true;

        return false;
    }
    private void RegresarACasillaActual()
    {
        if (casillaActual == null)
            return;

        haMovidoEsteTurno = false;

        BoardPiece bp = GetComponent<BoardPiece>();
        if (bp == null) bp = GetComponentInParent<BoardPiece>();
        if (bp != null && bp.Object != null && bp.Object.HasStateAuthority)
        {
            bp.HasMovedThisTurn = false;
        }

        casillaActual.estaOcupada = true;

        IniciarReposicionamiento(casillaActual);

        Rigidbody rb = GetComponent<Rigidbody>();

        if (rb != null)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.isKinematic = true;
        }

        SincronizarPosicionEnRed(casillaActual);

        Debug.Log(
            "[FichaRPG] Movimiento cancelado. La ficha regresó a su casilla actual: " +
            casillaActual.coordenadaX + ", " +
            casillaActual.coordenadaZ
        );
    }

    private void SincronizarPosicionEnRed(CasillaComponent casilla)
    {
        if (casilla == null)
            return;

        BoardPiece boardPiece = GetComponent<BoardPiece>();
        if (boardPiece != null && boardPiece.Object != null)
        {
            if (!boardPiece.Object.HasStateAuthority)
            {
                boardPiece.RPC_RequestColocarEnCasilla(casilla.coordenadaX, casilla.coordenadaZ);
            }
            else
            {
                boardPiece.RPC_MudarFichaATodos(casilla.coordenadaX, casilla.coordenadaZ);
            }
        }
    }
}