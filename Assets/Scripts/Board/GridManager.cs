using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class GridManager : MonoBehaviour
{
    public static GridManager Instance;

    [Header("Referencias del Tablero")]
    [SerializeField] private Transform padreTablero;
    [SerializeField] private LayerMask capaParedes;

    [Header("Configuración de Debug")]
    [Tooltip("Activa este modo para ver los colores de los bitmasks en todas las casillas a la vez.")]
    public bool modoDebugColorCasillas = false;
    [SerializeField] private bool modoDebugSpawns;
    [SerializeField] private Color colorSpawn = Color.green;
    [SerializeField] private Color colorRangoIluminado = Color.yellow; // Rango Válido
    
    [Header("Niebla de Guerra")]
    public int rangoVisionHeroes = 3;
    
    // Estructura de datos indexada (Diccionario) para acceder rápidamente por coordenadas
    private Dictionary<Vector2Int, CasillaComponent> diccionarioTablero = new Dictionary<Vector2Int, CasillaComponent>();
    public Dictionary<Vector2Int, CasillaComponent> DiccionarioTablero => diccionarioTablero;

    private List<CasillaComponent> casillasIluminadas = new List<CasillaComponent>();

    private void Awake()
    {
        Instance = this;
    }

    private void Start()
    {
        InicializarTableroManual();
        DesplegarHeroesEnSpawns();
        ActualizarNieblaDeGuerraGlobal();
    }

    public void InicializarTableroManual()
    {
        diccionarioTablero.Clear();
        CasillaComponent[] todasLasCasillas = Object.FindObjectsByType<CasillaComponent>(FindObjectsSortMode.None);

        foreach (var c in todasLasCasillas)
        {
            Vector2Int coord = new Vector2Int(c.coordenadaX, c.coordenadaZ);
            if (!diccionarioTablero.ContainsKey(coord))
            {
                diccionarioTablero[coord] = c;
            }
            else
            {
                Debug.LogWarning($"[GridManager] Coordenada duplicada detectada: {coord}. Casilla {c.name} ignorada.");
            }
        }
        
        Debug.Log($"[GridManager] Tablero inicializado manualmente con {diccionarioTablero.Count} casillas.");
        ActualizarColoresDebug();
    }

    /// <summary>
    /// Devuelve true si el destino está dentro de un círculo matemático en una grilla cuadrada.
    /// Usa la fórmula de la distancia al cuadrado (Pitágoras) usando solo enteros para evitar problemas de precisión flotante.
    /// </summary>
    public bool EsCasillaEnRangoCircularGrid(Vector2Int origen, Vector2Int destino, int rangoMaximo)
    {
        int dx = Mathf.Abs(origen.x - destino.x);
        int dy = Mathf.Abs(origen.y - destino.y);
        return (dx * dx) + (dy * dy) <= (rangoMaximo * rangoMaximo);
    }

    public void MostrarRangoMovimiento(CasillaComponent origen, int rango)
    {
        OcultarRangoMovimiento(); // Limpiamos primero por seguridad

        if (origen == null) return;

        Queue<CasillaComponent> cola = new Queue<CasillaComponent>();
        HashSet<CasillaComponent> visitadas = new HashSet<CasillaComponent>();

        cola.Enqueue(origen);
        visitadas.Add(origen);
        
        // Incluir el origen como válido siempre (permite cancelar el movimiento cayendo en el mismo lugar)
        casillasIluminadas.Add(origen);
        origen.SetearEstadoVisual("EnRango");

        Vector2Int[] direcciones = new Vector2Int[]
        {
            new Vector2Int(0, 1),   // Arriba (Norte)
            new Vector2Int(0, -1),  // Abajo (Sur)
            new Vector2Int(1, 0),   // Derecha (Este)
            new Vector2Int(-1, 0),  // Izquierda (Oeste)
            new Vector2Int(1, 1),   // Diagonal Arriba-Derecha
            new Vector2Int(1, -1),  // Diagonal Abajo-Derecha
            new Vector2Int(-1, 1),  // Diagonal Arriba-Izquierda
            new Vector2Int(-1, -1)  // Diagonal Abajo-Izquierda
        };

        while (cola.Count > 0)
        {
            CasillaComponent actual = cola.Dequeue();

            foreach (var dir in direcciones)
            {
                // Extraer transitabilidad: el camino está LIBRE si el bit correspondiente es 0 (o si es puerta=16)
                bool puedeNorte = ((actual.valorBitmask & 1) == 0) || ((actual.valorBitmask & 16) != 0);
                bool puedeEste = ((actual.valorBitmask & 2) == 0) || ((actual.valorBitmask & 16) != 0);
                bool puedeSur = ((actual.valorBitmask & 4) == 0) || ((actual.valorBitmask & 16) != 0);
                bool puedeOeste = ((actual.valorBitmask & 8) == 0) || ((actual.valorBitmask & 16) != 0);

                // Validación Estricta de Muros (Evitar traspasar paredes)
                if (dir == new Vector2Int(0, 1) && !puedeNorte) continue;
                if (dir == new Vector2Int(0, -1) && !puedeSur) continue;
                if (dir == new Vector2Int(1, 0) && !puedeEste) continue;
                if (dir == new Vector2Int(-1, 0) && !puedeOeste) continue;

                // Validación de Muros en Diagonales (No permite cortar esquinas a través de un muro)
                if (dir == new Vector2Int(1, 1) && (!puedeNorte || !puedeEste)) continue;
                if (dir == new Vector2Int(1, -1) && (!puedeSur || !puedeEste)) continue;
                if (dir == new Vector2Int(-1, 1) && (!puedeNorte || !puedeOeste)) continue;
                if (dir == new Vector2Int(-1, -1) && (!puedeSur || !puedeOeste)) continue;

                Vector2Int coordVecina = new Vector2Int(actual.coordenadaX + dir.x, actual.coordenadaZ + dir.y);

                if (diccionarioTablero.TryGetValue(coordVecina, out CasillaComponent vecino))
                {
                    // Saltar si es un obstáculo explícito
                    if (vecino.esObstaculo) continue;

                    bool vecinoPuedeNorte = ((vecino.valorBitmask & 1) == 0) || ((vecino.valorBitmask & 16) != 0);
                    bool vecinoPuedeEste = ((vecino.valorBitmask & 2) == 0) || ((vecino.valorBitmask & 16) != 0);
                    bool vecinoPuedeSur = ((vecino.valorBitmask & 4) == 0) || ((vecino.valorBitmask & 16) != 0);
                    bool vecinoPuedeOeste = ((vecino.valorBitmask & 8) == 0) || ((vecino.valorBitmask & 16) != 0);

                    // Validación bidireccional preventiva (el vecino tampoco debe tener un muro hacia nosotros)
                    if (dir == new Vector2Int(0, 1) && !vecinoPuedeSur) continue;
                    if (dir == new Vector2Int(0, -1) && !vecinoPuedeNorte) continue;
                    if (dir == new Vector2Int(1, 0) && !vecinoPuedeOeste) continue;
                    if (dir == new Vector2Int(-1, 0) && !vecinoPuedeEste) continue;

                    // Validación bidireccional en diagonales
                    if (dir == new Vector2Int(1, 1) && (!vecinoPuedeSur || !vecinoPuedeOeste)) continue;
                    if (dir == new Vector2Int(1, -1) && (!vecinoPuedeNorte || !vecinoPuedeOeste)) continue;
                    if (dir == new Vector2Int(-1, 1) && (!vecinoPuedeSur || !vecinoPuedeEste)) continue;
                    if (dir == new Vector2Int(-1, -1) && (!vecinoPuedeNorte || !vecinoPuedeEste)) continue;

                    if (!visitadas.Contains(vecino))
                    {
                        // 1. Verificar si está dentro del rango circular
                        Vector2Int origenCoord = new Vector2Int(origen.coordenadaX, origen.coordenadaZ);
                        if (EsCasillaEnRangoCircularGrid(origenCoord, coordVecina, rango))
                        {
                            visitadas.Add(vecino);
                            cola.Enqueue(vecino);

                            // Filtro de Destino: iluminar solo si no está ocupada (a menos que sea el propio origen)
                            if (!vecino.estaOcupada || vecino == origen)
                            {
                                if (!casillasIluminadas.Contains(vecino))
                                {
                                    vecino.SetearEstadoVisual("EnRango");
                                    casillasIluminadas.Add(vecino);
                                }
                            }
                        }
                    }
                }
            }
        }

        Debug.Log($"BFS ejecutado. Casillas válidas encontradas: {casillasIluminadas.Count}");
    }

    public void OcultarRangoMovimiento()
    {
        foreach (var casilla in casillasIluminadas)
        {
            casilla.SetearEstadoVisual("Apagado");
        }
        casillasIluminadas.Clear();
    }

    public bool EsCasillaValida(CasillaComponent casilla)
    {
        return casillasIluminadas.Contains(casilla);
    }

    private void DesplegarHeroesEnSpawns()
    {
        // Usa FichaRPG[] todasLasFichas = FindObjectsByType<FichaRPG>(FindObjectsSortMode.None); para encontrar todas las piezas que estén en la escena
        FichaRPG[] todasLasFichas = Object.FindObjectsByType<FichaRPG>(FindObjectsSortMode.None);
        
        // Recorre tus casillas indexadas y busca cuáles tienen el booleano esSpawnHeroe == true
        var casillasSpawn = diccionarioTablero.Values.Where(c => c.esSpawnHeroe).ToList();

        for (int i = 0; i < todasLasFichas.Length; i++)
        {
            if (i < casillasSpawn.Count)
            {
                FichaRPG ficha = todasLasFichas[i];
                CasillaComponent casillaSpawn = casillasSpawn[i];

                // Teletransporta físicamente cada ficha al centro de su respectiva casilla de spawn
                ficha.transform.position = casillaSpawn.ObtenerCentro();
                ficha.transform.rotation = Quaternion.identity;
                
                if (ficha.GetComponent<Rigidbody>() != null)
                {
                    ficha.GetComponent<Rigidbody>().isKinematic = true;
                }

                // Configura las variables de estado de inmediato en ese mismo instante
                casillaSpawn.estaOcupada = true;
                ficha.casillaActual = casillaSpawn;
            }
            else
            {
                Debug.LogWarning($"[GridManager] No hay suficientes casillas de spawn para el héroe {todasLasFichas[i].gameObject.name}.");
            }
        }
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        ActualizarColoresDebug();

        // Cuando cambias el checkbox en el GridManager, actualizamos todos los hijos en el editor
        CasillaComponent[] casillas = Object.FindObjectsByType<CasillaComponent>(FindObjectsSortMode.None);
        if (casillas != null)
        {
            foreach (var c in casillas)
            {
                if (c != null)
                {
                    c.ActualizarColorDebug(modoDebugColorCasillas);
                }
            }
        }
    }
#endif

    private void ActualizarColoresDebug()
    {
        if (padreTablero == null || diccionarioTablero == null) return;

        foreach (var casilla in diccionarioTablero.Values)
        {
            if (modoDebugSpawns && casilla.esSpawnHeroe)
            {
                casilla.SetearEstadoVisual("Hover");
            }
            // Eliminado el "Apagado" de aquí para que no interfiera con la Niebla
        }
    }

    public void ActualizarNieblaDeGuerraGlobal()
    {
        HashSet<CasillaComponent> casillasVisiblesColectivas = new HashSet<CasillaComponent>();
        FichaRPG[] todosLosHeroes = Object.FindObjectsByType<FichaRPG>(FindObjectsSortMode.None).Where(f => f.esHeroe).ToArray();

        Vector2Int[] direcciones = new Vector2Int[]
        {
            new Vector2Int(0, 1),   // Norte
            new Vector2Int(0, -1),  // Sur
            new Vector2Int(1, 0),   // Este
            new Vector2Int(-1, 0),  // Oeste
            new Vector2Int(1, 1),   // Diagonal Arriba-Derecha
            new Vector2Int(1, -1),  // Diagonal Abajo-Derecha
            new Vector2Int(-1, 1),  // Diagonal Arriba-Izquierda
            new Vector2Int(-1, -1)  // Diagonal Abajo-Izquierda
        };

        foreach (var heroe in todosLosHeroes)
        {
            if (heroe.casillaActual == null) continue;

            Queue<CasillaComponent> cola = new Queue<CasillaComponent>();
            HashSet<CasillaComponent> visitadas = new HashSet<CasillaComponent>();

            cola.Enqueue(heroe.casillaActual);
            visitadas.Add(heroe.casillaActual);
            casillasVisiblesColectivas.Add(heroe.casillaActual);

            while (cola.Count > 0)
            {
                CasillaComponent actual = cola.Dequeue();

                foreach (var dir in direcciones)
                {
                    bool puedeNorte = ((actual.valorBitmask & 1) == 0) || ((actual.valorBitmask & 16) != 0);
                    bool puedeEste = ((actual.valorBitmask & 2) == 0) || ((actual.valorBitmask & 16) != 0);
                    bool puedeSur = ((actual.valorBitmask & 4) == 0) || ((actual.valorBitmask & 16) != 0);
                    bool puedeOeste = ((actual.valorBitmask & 8) == 0) || ((actual.valorBitmask & 16) != 0);

                    if (dir == new Vector2Int(0, 1) && !puedeNorte) continue;
                    if (dir == new Vector2Int(0, -1) && !puedeSur) continue;
                    if (dir == new Vector2Int(1, 0) && !puedeEste) continue;
                    if (dir == new Vector2Int(-1, 0) && !puedeOeste) continue;

                    if (dir == new Vector2Int(1, 1) && (!puedeNorte || !puedeEste)) continue;
                    if (dir == new Vector2Int(1, -1) && (!puedeSur || !puedeEste)) continue;
                    if (dir == new Vector2Int(-1, 1) && (!puedeNorte || !puedeOeste)) continue;
                    if (dir == new Vector2Int(-1, -1) && (!puedeSur || !puedeOeste)) continue;

                    Vector2Int coordVecina = new Vector2Int(actual.coordenadaX + dir.x, actual.coordenadaZ + dir.y);

                    if (diccionarioTablero.TryGetValue(coordVecina, out CasillaComponent vecino))
                    {
                        bool vecinoPuedeNorte = ((vecino.valorBitmask & 1) == 0) || ((vecino.valorBitmask & 16) != 0);
                        bool vecinoPuedeEste = ((vecino.valorBitmask & 2) == 0) || ((vecino.valorBitmask & 16) != 0);
                        bool vecinoPuedeSur = ((vecino.valorBitmask & 4) == 0) || ((vecino.valorBitmask & 16) != 0);
                        bool vecinoPuedeOeste = ((vecino.valorBitmask & 8) == 0) || ((vecino.valorBitmask & 16) != 0);

                        if (dir == new Vector2Int(0, 1) && !vecinoPuedeSur) continue;
                        if (dir == new Vector2Int(0, -1) && !vecinoPuedeNorte) continue;
                        if (dir == new Vector2Int(1, 0) && !vecinoPuedeOeste) continue;
                        if (dir == new Vector2Int(-1, 0) && !vecinoPuedeEste) continue;

                        if (dir == new Vector2Int(1, 1) && (!vecinoPuedeSur || !vecinoPuedeOeste)) continue;
                        if (dir == new Vector2Int(1, -1) && (!vecinoPuedeNorte || !vecinoPuedeOeste)) continue;
                        if (dir == new Vector2Int(-1, 1) && (!vecinoPuedeSur || !vecinoPuedeEste)) continue;
                        if (dir == new Vector2Int(-1, -1) && (!vecinoPuedeNorte || !vecinoPuedeEste)) continue;

                        if (!visitadas.Contains(vecino))
                        {
                            Vector2Int origenCoord = new Vector2Int(heroe.casillaActual.coordenadaX, heroe.casillaActual.coordenadaZ);
                            if (EsCasillaEnRangoCircularGrid(origenCoord, coordVecina, rangoVisionHeroes))
                            {
                                visitadas.Add(vecino);
                                
                                // ¡Ahora sí la podemos ver! Se añade al mapa iluminado (incluso si es un obstáculo)
                                casillasVisiblesColectivas.Add(vecino);

                                // Solo seguimos explorando a través de ella si NO es un obstáculo
                                if (!vecino.esObstaculo)
                                {
                                    cola.Enqueue(vecino);
                                }
                            }
                        }
                    }
                }
            }
        }

        foreach (var casilla in diccionarioTablero.Values)
        {
            if (casillasVisiblesColectivas.Contains(casilla))
            {
                casilla.estaEnNiebla = false;
                casilla.SetearEstadoVisual("Visible");
            }
            else
            {
                casilla.estaEnNiebla = true;
                casilla.SetearEstadoVisual("Niebla");
            }
        }

        if (KeyWinManager.Instance != null)
        {
            KeyWinManager.Instance.ActualizarVisibilidadNiebla(casillasVisiblesColectivas);
        }
    }

}
