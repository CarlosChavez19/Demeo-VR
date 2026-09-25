using System.Collections.Generic;
using UnityEngine;
using Fusion;
using DemeoVR.Gameplay;

public class BoardCombatManager : NetworkBehaviour
{
    public static BoardCombatManager Instance;

    [Header("Configuración")]
    [SerializeField] private int costoAPAtacar = 1;

    private int ultimoTurnoDetectado = -999;

    private void Awake()
    {
        Instance = this;
    }

    public override void Spawned()
    {
        Debug.Log(
            "[BoardCombatManager] Spawned. " +
            "StateAuthority = " + Object.HasStateAuthority +
            " | LocalPlayer = " + Runner.LocalPlayer
        );
    }

    private void Update()
    {
        if (!Object.HasStateAuthority)
            return;

        if (TurnManager.Instance == null)
            return;

        int turnoActual = TurnManager.Instance.CurrentTurnIndex;

        if (turnoActual != ultimoTurnoDetectado)
        {
            ultimoTurnoDetectado = turnoActual;
            PrepararTurnoActual();
        }
    }

    private void PrepararTurnoActual()
    {
        if (GameplayManager.Instance == null)
            return;

        if (GameplayManager.Instance.TurnOrder == null ||
            GameplayManager.Instance.TurnOrder.Count == 0)
            return;

        int index = TurnManager.Instance.CurrentTurnIndex;

        if (index < 0 || index >= GameplayManager.Instance.TurnOrder.Count)
            index = 0;

        GameplayRoleCache.PlayerInfo jugadorTurno =
            GameplayManager.Instance.TurnOrder[index];

        Debug.Log(
            "[BoardCombatManager HOST] Nuevo turno detectado. " +
            "Jugador = " + jugadorTurno.PlayerName +
            " | Rol = " + jugadorTurno.PlayerRole
        );

        if (EsRolHeroe(jugadorTurno.PlayerRole))
        {
            FichaRPG fichaHeroe = BuscarHeroePorRol(jugadorTurno.PlayerRole);

            if (fichaHeroe == null)
            {
                Debug.LogWarning(
                    "[BoardCombatManager HOST] No se encontró ficha del héroe para rol = " +
                    jugadorTurno.PlayerRole
                );

                return;
            }

            fichaHeroe.ReiniciarTurno();

            BoardPiece statsHeroe = ObtenerBoardPiece(fichaHeroe);

            if (statsHeroe != null)
            {
                statsHeroe.StartTurn();
            }
            else
            {
                Debug.LogWarning(
                    "[BoardCombatManager HOST] La ficha del héroe no tiene BoardPiece. Ficha = " +
                    fichaHeroe.name
                );
            }
        }

        if (jugadorTurno.PlayerRole == "Dungeon Master")
        {
            ReiniciarAtaquesDeEnemigos();
        }
    }

    private void ReiniciarAtaquesDeEnemigos()
    {
        FichaEnemigoAI[] enemigos =
            FindObjectsByType<FichaEnemigoAI>(FindObjectsSortMode.None);

        foreach (FichaEnemigoAI enemigo in enemigos)
        {
            BoardPiece statsEnemigo = ObtenerBoardPiece(enemigo);

            if (statsEnemigo != null)
            {
                statsEnemigo.StartTurn();
            }
        }

        Debug.Log(
            "[BoardCombatManager HOST] Enemigos preparados para atacar. Cantidad = " +
            enemigos.Length
        );
    }

    // =========================================================
    // ATAQUE DEL HÉROE
    // =========================================================

    public void RequestHeroAttackFromButton()
    {
        if (GameplayManager.Instance == null || TurnManager.Instance == null)
        {
            Debug.LogWarning("[BoardCombatManager] No hay GameplayManager o TurnManager.");
            return;
        }

        if (!TurnManager.Instance.IsMyTurn())
        {
            Debug.LogWarning("[BoardCombatManager] No puedes atacar porque no es tu turno.");
            return;
        }

        string rolLocal = GameplayManager.Instance.LocalPlayerRole;

        if (!EsRolHeroe(rolLocal))
        {
            Debug.LogWarning("[BoardCombatManager] Solo los héroes pueden atacar con este botón.");
            return;
        }

        FichaRPG fichaHeroe = BuscarHeroePorRol(rolLocal);

        if (fichaHeroe == null)
        {
            Debug.LogWarning(
                "[BoardCombatManager] No se encontró ficha para el rol local = " +
                rolLocal
            );

            return;
        }

        FichaEnemigoAI enemigo = BuscarEnemigoAdyacenteA(fichaHeroe);

        if (enemigo == null)
        {
            Debug.LogWarning(
                "[BoardCombatManager] No hay enemigo en casilla adyacente. " +
                "Recuerda: solo arriba, abajo, izquierda o derecha."
            );

            return;
        }

        Debug.Log(
            "[BoardCombatManager] Solicitando ataque al host. " +
            "Heroe = " + rolLocal +
            " | Enemigo = " + enemigo.name +
            " | EnemyX = " + enemigo.coordenadaX +
            " | EnemyZ = " + enemigo.coordenadaZ
        );

        RPC_RequestHeroAttack(
            Runner.LocalPlayer,
            enemigo.coordenadaX,
            enemigo.coordenadaZ
        );
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_RequestHeroAttack(PlayerRef requester, int enemyX, int enemyZ)
    {
        Debug.Log(
            "[BoardCombatManager HOST] Petición de ataque recibida. " +
            "Requester = " + requester +
            " | EnemyX = " + enemyX +
            " | EnemyZ = " + enemyZ
        );

        if (!EsTurnoDePlayer(requester))
        {
            Debug.LogWarning("[BoardCombatManager HOST] Ataque rechazado. No es turno del jugador.");
            return;
        }

        string rolRequester = ObtenerRolDePlayer(requester);

        if (!EsRolHeroe(rolRequester))
        {
            Debug.LogWarning(
                "[BoardCombatManager HOST] Ataque rechazado. El requester no es héroe. Rol = " +
                rolRequester
            );

            return;
        }

        FichaRPG fichaHeroe = BuscarHeroePorRol(rolRequester);

        if (fichaHeroe == null)
        {
            Debug.LogWarning(
                "[BoardCombatManager HOST] No se encontró ficha del héroe. Rol = " +
                rolRequester
            );

            return;
        }

        BoardPiece statsHeroe = ObtenerBoardPiece(fichaHeroe);

        if (statsHeroe == null)
        {
            Debug.LogWarning(
                "[BoardCombatManager HOST] La ficha del héroe no tiene BoardPiece."
            );

            return;
        }

        if (!statsHeroe.CanAttackThisTurn())
        {
            Debug.LogWarning(
                "[BoardCombatManager HOST] Ataque rechazado. Ya atacó este turno o no tiene AP. " +
                "AP = " + statsHeroe.CurrentAP +
                " | Ya atacó = " + statsHeroe.HasAttackedThisTurn
            );

            return;
        }

        FichaEnemigoAI enemigo = BuscarEnemigoPorCoordenada(enemyX, enemyZ);

        if (enemigo == null)
        {
            Debug.LogWarning("[BoardCombatManager HOST] No se encontró enemigo en esa coordenada.");
            return;
        }

        if (!EstanAdyacentes(fichaHeroe, enemigo))
        {
            Debug.LogWarning(
                "[BoardCombatManager HOST] Ataque rechazado. " +
                "El enemigo no está arriba, abajo, izquierda o derecha."
            );

            return;
        }

        BoardPiece statsEnemigo = ObtenerBoardPiece(enemigo);

        if (statsEnemigo == null)
        {
            Debug.LogWarning(
                "[BoardCombatManager HOST] El enemigo no tiene BoardPiece."
            );

            return;
        }

        if (statsEnemigo.CurrentHealth <= 0)
        {
            Debug.LogWarning("[BoardCombatManager HOST] El enemigo ya está muerto.");
            return;
        }

        if (!statsHeroe.ConsumeAP(costoAPAtacar))
        {
            Debug.LogWarning("[BoardCombatManager HOST] Ataque cancelado por AP insuficiente.");
            return;
        }

        int damageFisico = statsHeroe.PhysicalDamage;
        int damageMagico = statsHeroe.MagicDamage;

        statsEnemigo.TakeDamage(damageFisico, damageMagico);
        statsHeroe.MarkAttackUsed();

        if (HeroCardManager.Instance != null)
        {
            HeroCardManager.Instance.TryRewardCardIfEnemyKilled(
                statsEnemigo,
                rolRequester
            );
        }
        Debug.Log(
            "[BoardCombatManager HOST] Ataque de héroe ejecutado. " +
            "Heroe = " + fichaHeroe.name +
            " | Enemigo = " + enemigo.name +
            " | Daño físico = " + damageFisico +
            " | Daño mágico = " + damageMagico +
            " | HP enemigo = " + statsEnemigo.CurrentHealth
        );
    }

    // =========================================================
    // ATAQUE DE ENEMIGOS DEL DM
    // =========================================================

    public void RequestEnemyAttacksBeforeDMEndTurn()
    {
        if (GameplayManager.Instance == null || TurnManager.Instance == null)
        {
            Debug.LogWarning("[BoardCombatManager] No hay GameplayManager o TurnManager.");
            return;
        }

        if (GameplayManager.Instance.LocalPlayerRole != "Dungeon Master")
        {
            Debug.LogWarning("[BoardCombatManager] Solo el DM puede ejecutar ataques enemigos.");
            return;
        }

        if (!TurnManager.Instance.IsMyTurn())
        {
            Debug.LogWarning("[BoardCombatManager] No es turno del DM.");
            return;
        }

        Debug.Log("[BoardCombatManager] Solicitando ataques enemigos al host.");

        RPC_RequestEnemyAttacks(Runner.LocalPlayer);
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_RequestEnemyAttacks(PlayerRef requester)
    {
        string rolRequester = ObtenerRolDePlayer(requester);

        Debug.Log(
            "[BoardCombatManager HOST] Petición de ataques enemigos recibida. " +
            "Requester = " + requester +
            " | Rol = " + rolRequester
        );

        if (rolRequester != "Dungeon Master")
        {
            Debug.LogWarning("[BoardCombatManager HOST] Rechazado. El requester no es DM.");
            return;
        }

        if (!EsTurnoDePlayer(requester))
        {
            Debug.LogWarning("[BoardCombatManager HOST] Rechazado. No es turno del DM.");
            return;
        }

        FichaEnemigoAI[] enemigos =
            FindObjectsByType<FichaEnemigoAI>(FindObjectsSortMode.None);

        FichaRPG[] heroesActivos = ObtenerHeroesActivos();

        Debug.Log(
            "[BoardCombatManager HOST] Ejecutando ataques enemigos. " +
            "Enemigos = " + enemigos.Length +
            " | Heroes activos = " + heroesActivos.Length
        );

        foreach (FichaEnemigoAI enemigo in enemigos)
        {
            BoardPiece statsEnemigo = ObtenerBoardPiece(enemigo);

            if (statsEnemigo == null)
            {
                Debug.LogWarning(
                    "[BoardCombatManager HOST] Enemigo sin BoardPiece. Enemigo = " +
                    enemigo.name
                );

                continue;
            }

            if (!statsEnemigo.CanAttackThisTurn())
            {
                Debug.Log(
                    "[BoardCombatManager HOST] Enemigo no puede atacar. " +
                    "Enemigo = " + enemigo.name +
                    " | AP = " + statsEnemigo.CurrentAP +
                    " | Ya atacó = " + statsEnemigo.HasAttackedThisTurn
                );

                continue;
            }

            if (statsEnemigo.IsStunned)
            {
                Debug.Log(
                    "[BoardCombatManager HOST] Enemigo aturdido. Pierde su ataque. Enemigo = " +
                    enemigo.name
                );

                statsEnemigo.ClearStun();
                continue;
            }
            
            FichaRPG heroeObjetivo = BuscarHeroeAdyacenteAlEnemigo(enemigo, heroesActivos);

            if (heroeObjetivo == null)
            {
                Debug.Log(
                    "[BoardCombatManager HOST] Enemigo sin héroe adyacente. Enemigo = " +
                    enemigo.name
                );

                continue;
            }

            BoardPiece statsHeroe = ObtenerBoardPiece(heroeObjetivo);

            if (statsHeroe == null)
            {
                Debug.LogWarning(
                    "[BoardCombatManager HOST] Héroe sin BoardPiece. Heroe = " +
                    heroeObjetivo.name
                );

                continue;
            }

            if (statsHeroe.CurrentHealth <= 0)
            {
                Debug.Log(
                    "[BoardCombatManager HOST] Héroe ya está muerto. Heroe = " +
                    heroeObjetivo.name
                );

                continue;
            }

            if (!statsEnemigo.ConsumeAP(costoAPAtacar))
            {
                Debug.LogWarning(
                    "[BoardCombatManager HOST] Enemigo no pudo consumir AP. Enemigo = " +
                    enemigo.name
                );

                continue;
            }

            int damageFisico = statsEnemigo.PhysicalDamage;
            int damageMagico = statsEnemigo.MagicDamage;

            statsHeroe.TakeDamage(damageFisico, damageMagico);
            statsEnemigo.MarkAttackUsed();

            Debug.Log(
                "[BoardCombatManager HOST] Enemigo atacó. " +
                "Enemigo = " + enemigo.name +
                " | Heroe = " + heroeObjetivo.name +
                " | Daño físico = " + damageFisico +
                " | Daño mágico = " + damageMagico +
                " | HP héroe = " + statsHeroe.CurrentHealth
            );
        }
    }

    // =========================================================
    // FUNCIONES AUXILIARES
    // =========================================================

    private bool EsRolHeroe(string rol)
    {
        return rol == "Heroe 1" || rol == "Heroe 2";
    }

    private FichaRPG BuscarHeroePorRol(string rol)
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

    private FichaRPG[] ObtenerHeroesActivos()
    {
        List<FichaRPG> heroes = new List<FichaRPG>();

        FichaRPG[] fichas =
            FindObjectsByType<FichaRPG>(FindObjectsSortMode.None);

        foreach (FichaRPG ficha in fichas)
        {
            if (!ficha.esHeroe)
                continue;

            if (EsRolActivo(ficha.RolPropietario))
            {
                heroes.Add(ficha);
            }
        }

        return heroes.ToArray();
    }

    private bool EsRolActivo(string rol)
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

    private FichaEnemigoAI BuscarEnemigoAdyacenteA(FichaRPG heroe)
    {
        FichaEnemigoAI[] enemigos =
            FindObjectsByType<FichaEnemigoAI>(FindObjectsSortMode.None);

        foreach (FichaEnemigoAI enemigo in enemigos)
        {
            BoardPiece statsEnemigo = ObtenerBoardPiece(enemigo);

            if (statsEnemigo != null && statsEnemigo.CurrentHealth <= 0)
                continue;

            if (EstanAdyacentes(heroe, enemigo))
                return enemigo;
        }

        return null;
    }

    private FichaEnemigoAI BuscarEnemigoPorCoordenada(int x, int z)
    {
        FichaEnemigoAI[] enemigos =
            FindObjectsByType<FichaEnemigoAI>(FindObjectsSortMode.None);

        foreach (FichaEnemigoAI enemigo in enemigos)
        {
            if (enemigo.coordenadaX == x && enemigo.coordenadaZ == z)
                return enemigo;
        }

        return null;
    }

    private FichaRPG BuscarHeroeAdyacenteAlEnemigo(FichaEnemigoAI enemigo, FichaRPG[] heroes)
    {
        foreach (FichaRPG heroe in heroes)
        {
            if (EstanAdyacentes(heroe, enemigo))
                return heroe;
        }

        return null;
    }

    private bool EstanAdyacentes(FichaRPG heroe, FichaEnemigoAI enemigo)
    {
        if (heroe == null || enemigo == null)
            return false;

        if (heroe.casillaActual == null)
            return false;

        int dx = Mathf.Abs(heroe.casillaActual.coordenadaX - enemigo.coordenadaX);
        int dz = Mathf.Abs(heroe.casillaActual.coordenadaZ - enemigo.coordenadaZ);

        // Solo cuenta arriba, abajo, izquierda o derecha.
        // Diagonal NO cuenta.
        return dx + dz == 1;
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

    private bool EsTurnoDePlayer(PlayerRef player)
    {
        if (GameplayManager.Instance == null || TurnManager.Instance == null)
            return false;

        if (GameplayManager.Instance.TurnOrder == null ||
            GameplayManager.Instance.TurnOrder.Count == 0)
            return false;

        int index = TurnManager.Instance.CurrentTurnIndex;

        if (index < 0 || index >= GameplayManager.Instance.TurnOrder.Count)
            index = 0;

        return GameplayManager.Instance.TurnOrder[index].PlayerRef == player;
    }

    private string ObtenerRolDePlayer(PlayerRef player)
    {
        if (GameplayManager.Instance != null &&
            GameplayManager.Instance.TurnOrder != null)
        {
            foreach (GameplayRoleCache.PlayerInfo info in GameplayManager.Instance.TurnOrder)
            {
                if (info.PlayerRef == player)
                    return info.PlayerRole;
            }
        }

        foreach (GameplayRoleCache.PlayerInfo info in GameplayRoleCache.Players)
        {
            if (info.PlayerRef == player)
                return info.PlayerRole;
        }

        return "Desconocido";
    }
}