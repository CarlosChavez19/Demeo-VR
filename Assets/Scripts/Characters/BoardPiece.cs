using UnityEngine;
using Fusion;

namespace DemeoVR.Gameplay
{
    public class BoardPiece : NetworkBehaviour
    {
        [Header("Estadísticas de Diseño")]
        public PieceData baseData;

        [Header("Estado Actual Sincronizado")]
        [Networked] public int CurrentHealth { get; set; }
        [Networked] public int CurrentAP { get; set; }
        [Networked] public int CurrentLevel { get; set; }
        [Networked] public int CurrentXP { get; set; }
        [Networked] public bool RewardGivenOnDeath { get; set; }
        [Networked] public bool IsStunned { get; set; }
        [Networked] public bool IsFrozen { get; set; }
        [Networked] public int DamageReductionPercent { get; set; }

        [Header("Control de Combate")]
        [Networked] public bool HasAttackedThisTurn { get; set; }
        [Networked] public bool HasMovedThisTurn { get; set; }
        [Networked] public bool IsDead { get; set; }

        public int MaxHealth =>
            baseData != null ? baseData.maxHealth + ((CurrentLevel - 1) * baseData.hpGrowth) : 0;

        public int PhysicalDamage =>
            baseData != null ? baseData.physicalDamage + ((CurrentLevel - 1) * baseData.physicalDamageGrowth) : 0;

        public int MagicDamage =>
            baseData != null ? baseData.magicDamage + ((CurrentLevel - 1) * baseData.magicDamageGrowth) : 0;

        public int Armor =>
            baseData != null ? baseData.armor + ((CurrentLevel - 1) * baseData.armorGrowth) : 0;

        public int MagicResistance =>
            baseData != null ? baseData.magicResistance + ((CurrentLevel - 1) * baseData.magicResistanceGrowth) : 0;

        public PieceType Type =>
            baseData != null ? baseData.pieceType : PieceType.Enemigo;

        public override void Spawned()
        {
            if (!Object.HasStateAuthority)
            {
                Debug.Log(
                    "[BoardPiece CLIENT] Spawned recibido. " +
                    "Objeto = " + gameObject.name +
                    " | HP = " + CurrentHealth +
                    " | AP = " + CurrentAP
                );

                return;
            }

            CurrentLevel = 1;
            CurrentXP = 0;
            HasAttackedThisTurn = false;
            HasMovedThisTurn = false;

            RewardGivenOnDeath = false;
            IsStunned = false;
            IsFrozen = false;
            DamageReductionPercent = 0;
            IsDead = false;

            if (baseData != null)
            {
                CurrentHealth = MaxHealth;
                CurrentAP = baseData.maxAP;

                Debug.Log(
                    "[BoardPiece HOST] Stats inicializados. " +
                    "Objeto = " + gameObject.name +
                    " | Tipo = " + Type +
                    " | HP = " + CurrentHealth +
                    " | AP = " + CurrentAP +
                    " | Nivel = " + CurrentLevel
                );
            }
            else
            {
                Debug.LogWarning(
                    "[BoardPiece HOST] Falta asignar PieceData en baseData. Objeto = " +
                    gameObject.name
                );
            }
        }

        public virtual void StartTurn()
        {
            if (!Object.HasStateAuthority)
                return;

            HasAttackedThisTurn = false;
            HasMovedThisTurn = false;

            if (baseData != null)
            {
                CurrentAP = baseData.maxAP;
            }

            if (IsFrozen)
            {
                CurrentAP = Mathf.Max(0, CurrentAP - 1);
                IsFrozen = false;

                Debug.Log("[BoardPiece HOST] Congelado: AP reducido en 1. Objeto = " + gameObject.name);
            }

            if (DamageReductionPercent > 0)
            {
                DamageReductionPercent = 0;

                Debug.Log("[BoardPiece HOST] Reducción de daño terminada. Objeto = " + gameObject.name);
            }

            Debug.Log(
                "[BoardPiece HOST] Inicio de turno. " +
                "Objeto = " + gameObject.name +
                " | AP = " + CurrentAP
            );
        }

        public bool CanAttackThisTurn()
        {
            return !HasAttackedThisTurn && CurrentAP > 0 && CurrentHealth > 0;
        }

        public void MarkAttackUsed()
        {
            if (!Object.HasStateAuthority)
                return;

            HasAttackedThisTurn = true;

            Debug.Log(
                "[BoardPiece HOST] Ataque marcado como usado. Objeto = " +
                gameObject.name
            );
        }

        public virtual void TakeDamage(int physDmg, int magicDmg)
        {
            if (!Object.HasStateAuthority)
            {
                Debug.LogWarning(
                    "[BoardPiece] TakeDamage ignorado porque este cliente no tiene StateAuthority. Objeto = " +
                    gameObject.name
                );

                return;
            }

            int finalPhysDmg = Mathf.Max(0, physDmg - Armor);
            int finalMagicDmg = Mathf.Max(0, magicDmg - MagicResistance);
            int totalDamage = finalPhysDmg + finalMagicDmg;

            if (DamageReductionPercent > 0 && totalDamage > 0)
            {
                int damageOriginal = totalDamage;

                totalDamage = Mathf.CeilToInt(
                    totalDamage * ((100f - DamageReductionPercent) / 100f)
                );

                Debug.Log(
                    "[BoardPiece HOST] Reducción aplicada. " +
                    "Daño original = " + damageOriginal +
                    " | Reducción = " + DamageReductionPercent + "%" +
                    " | Daño final = " + totalDamage
                );
            }

            CurrentHealth -= totalDamage;

            Debug.Log(
                "[BoardPiece HOST] Daño recibido. " +
                "Objeto = " + gameObject.name +
                " | Daño físico final = " + finalPhysDmg +
                " | Daño mágico final = " + finalMagicDmg +
                " | Daño total = " + totalDamage +
                " | HP actual = " + CurrentHealth
            );

            if (CurrentHealth <= 0)
            {
                CurrentHealth = 0;
                Die();
            }
        }

        protected virtual void Die()
        {
            if (!Object.HasStateAuthority)
                return;

            if (IsDead)
                return;

            IsDead = true;

            Debug.LogWarning(
                "[BoardPiece HOST] " + gameObject.name + " ha muerto."
            );

            FichaEnemigoAI enemigo = GetComponent<FichaEnemigoAI>();

            if (enemigo == null)
                enemigo = GetComponentInParent<FichaEnemigoAI>();

            if (enemigo != null)
            {
                RPC_EliminarEnemigoMuerto(
                    enemigo.coordenadaX,
                    enemigo.coordenadaZ
                );

                Debug.LogWarning(
                    "[BoardPiece HOST] Enemigo muerto enviado a ocultar en todos. " +
                    "X = " + enemigo.coordenadaX +
                    " | Z = " + enemigo.coordenadaZ
                );
            }
        }
        public virtual bool ConsumeAP(int cost)
        {
            if (!Object.HasStateAuthority)
            {
                Debug.LogWarning(
                    "[BoardPiece] ConsumeAP ignorado porque este cliente no tiene StateAuthority. Objeto = " +
                    gameObject.name
                );

                return false;
            }

            if (CurrentAP >= cost)
            {
                CurrentAP -= cost;

                Debug.Log(
                    "[BoardPiece HOST] AP consumido. " +
                    "Objeto = " + gameObject.name +
                    " | Costo = " + cost +
                    " | AP restante = " + CurrentAP
                );

                return true;
            }

            Debug.LogWarning(
                "[BoardPiece HOST] AP insuficiente. " +
                "Objeto = " + gameObject.name +
                " | Requiere = " + cost +
                " | Disponible = " + CurrentAP
            );

            return false;
        }

        public void EarnXP(int amount)
        {
            if (!Object.HasStateAuthority || baseData == null)
                return;

            CurrentXP += amount;

            Debug.Log(
                "[BoardPiece HOST] XP ganada. " +
                "Objeto = " + gameObject.name +
                " | XP ganada = " + amount +
                " | XP actual = " + CurrentXP
            );

            while (CurrentLevel <= baseData.xpRequirements.Length &&
                   CurrentXP >= baseData.xpRequirements[CurrentLevel - 1])
            {
                CurrentXP -= baseData.xpRequirements[CurrentLevel - 1];
                CurrentLevel++;
                CurrentHealth = MaxHealth;

                Debug.Log(
                    "[BoardPiece HOST] Subió de nivel. " +
                    "Objeto = " + gameObject.name +
                    " | Nivel = " + CurrentLevel +
                    " | HP restaurado = " + CurrentHealth
                );
            }
        }
        public bool TryMarkRewardGiven()
        {
            if (!Object.HasStateAuthority)
                return false;

            if (RewardGivenOnDeath)
                return false;

            RewardGivenOnDeath = true;
            return true;
        }

        public void ApplyStun()
        {
            if (!Object.HasStateAuthority)
                return;

            IsStunned = true;

            Debug.Log("[BoardPiece HOST] Aturdido aplicado. Objeto = " + gameObject.name);
        }

        public void ClearStun()
        {
            if (!Object.HasStateAuthority)
                return;

            IsStunned = false;

            Debug.Log("[BoardPiece HOST] Aturdido eliminado. Objeto = " + gameObject.name);
        }

        public void ApplyFrozen()
        {
            if (!Object.HasStateAuthority)
                return;

            IsFrozen = true;

            Debug.Log("[BoardPiece HOST] Congelado aplicado. Objeto = " + gameObject.name);
        }

        public void ApplyDamageReduction(int percent)
        {
            if (!Object.HasStateAuthority)
                return;

            DamageReductionPercent = percent;

            Debug.Log(
                "[BoardPiece HOST] Reducción de daño aplicada. " +
                "Objeto = " + gameObject.name +
                " | Reducción = " + percent + "%"
            );
        }
        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        private void RPC_EliminarEnemigoMuerto(int enemyX, int enemyZ)
        {
            // 1. Liberar la casilla en todos los clientes
            CasillaComponent[] casillas =
                FindObjectsByType<CasillaComponent>(FindObjectsSortMode.None);

            foreach (CasillaComponent casilla in casillas)
            {
                if (casilla.coordenadaX == enemyX &&
                    casilla.coordenadaZ == enemyZ)
                {
                    casilla.estaOcupada = false;

                    Debug.Log(
                        "[BoardPiece TODOS] Casilla liberada por muerte de enemigo. " +
                        "X = " + enemyX +
                        " | Z = " + enemyZ
                    );

                    break;
                }
            }

            // 2. Ocultar visualmente el enemigo en todos
            Renderer[] renderers = GetComponentsInChildren<Renderer>(true);

            foreach (Renderer renderer in renderers)
            {
                renderer.enabled = false;
            }

            // 3. Desactivar colliders en todos
            Collider[] colliders = GetComponentsInChildren<Collider>(true);

            foreach (Collider collider in colliders)
            {
                collider.enabled = false;
            }

            // 4. Congelar física
            Rigidbody rb = GetComponent<Rigidbody>();

            if (rb != null)
            {
                rb.isKinematic = true;
            }

            // 5. Desactivar IA del enemigo
            FichaEnemigoAI enemigo = GetComponent<FichaEnemigoAI>();

            if (enemigo == null)
                enemigo = GetComponentInParent<FichaEnemigoAI>();

            if (enemigo != null)
            {
                enemigo.enabled = false;
            }

            Debug.Log(
                "[BoardPiece TODOS] Enemigo ocultado correctamente en este cliente. " +
                "Objeto = " + gameObject.name
            );
        }

        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        public void RPC_RequestColocarEnCasilla(int x, int z)
        {
            FichaRPG ficha = GetComponent<FichaRPG>();
            if (ficha != null && ficha.casillaActual != null)
            {
                if (ficha.casillaActual.coordenadaX != x || ficha.casillaActual.coordenadaZ != z)
                {
                    HasMovedThisTurn = true;
                }
                else
                {
                    HasMovedThisTurn = false;
                }
            }
            RPC_MudarFichaATodos(x, z);
        }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    public void RPC_MudarFichaATodos(int x, int z)
    {
        FichaRPG ficha = GetComponent<FichaRPG>();
        if (ficha != null)
        {
            if (GridManager.Instance != null && GridManager.Instance.DiccionarioTablero.TryGetValue(new Vector2Int(x, z), out CasillaComponent casilla))
            {
                ficha.ColocarEnCasillaDesdeRed(casilla);
            }
        }
    }
}
}