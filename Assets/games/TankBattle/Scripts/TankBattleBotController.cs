using UnityEngine;

namespace TankBattle
{
    public class TankBattleBotController : MonoBehaviour
    {
        private TankBattleBotSettings settings;
        private TankBattlePlayer owner;
        private TankBattlePlayer targetEnemy;
        private float nextDecisionTime;
        private Vector2 desiredMoveVector;
        private Vector2 currentMoveVector;
        private float lastShootTime = -999f;
        private float shootPauseEndTime = 0f;
        private float shootTapEndTime = -999f;
        private float nextDodgeAllowedTime = 0f;
        
        private TankBattleMapGenerator mapGen;
        private float cellSize;
        private Vector2 gridOrigin;
        private int gridCols;
        private int gridRows;
        
        private int currentGridIndex = -1;
        private Vector2 currentGridCenter;
        
        private TankBattlePathfinder pathfinder;
        private Vector2 lastPosition;
        private float stuckCheckTime;
        private int stuckCounter;
        
        private Vector2 cachedWaypoint;
        private float waypointCacheTime;
        private Vector2 randomExploreTarget;
        private float exploreTargetSetTime;

        public Vector2 MoveVector => currentMoveVector;
        public bool WantsToShoot { get; private set; }

        public void Initialize(TankBattleBotSettings botSettings, TankBattlePlayer ownerPlayer)
        {
            settings = botSettings;
            owner = ownerPlayer;
            lastShootTime = -999f;
            shootTapEndTime = -999f;
            nextDodgeAllowedTime = 0f;
            
            mapGen = FindAnyObjectByType<TankBattleMapGenerator>();
            
            if (mapGen != null)
            {
                cellSize = mapGen.cellSize + mapGen.gapSize;
                gridCols = mapGen.lastCols;
                gridRows = mapGen.lastRows;
                
                float innerW = gridCols * cellSize - mapGen.gapSize;
                float innerH = gridRows * cellSize - mapGen.gapSize;
                gridOrigin = new Vector2(-innerW * 0.5f, -innerH * 0.5f);
            }
            else
            {
                cellSize = 1.12f;
                gridCols = 8;
                gridRows = 6;
                gridOrigin = Vector2.zero;
            }
            
            pathfinder = new TankBattlePathfinder(cellSize, gridOrigin, gridCols, gridRows);
            lastPosition = owner.Position;
            stuckCheckTime = Time.time;
            waypointCacheTime = 0;
            exploreTargetSetTime = 0;
            nextDecisionTime = Time.time + Random.Range(0.04f, 0.11f);
        }

        private void Update()
        {
            if (owner == null || !owner.IsAlive)
            {
                currentMoveVector = Vector2.zero;
                WantsToShoot = false;
                return;
            }

            WantsToShoot = Time.time <= shootTapEndTime;

            if (Time.time >= nextDecisionTime)
            {
                MakeDecision();
                ScheduleNextDecision();
            }

            SmoothMovement(Time.deltaTime);
        }

        private void MakeDecision()
        {
            bool isInShootCooldown = Time.time < shootPauseEndTime;

            FindTarget();
            UpdateGridPosition();

            if (HandleBulletDodging()) 
            {
                return;
            }

            if (targetEnemy == null || !targetEnemy.IsAlive)
            {
                SnapToGridCenter();
                AvoidMovingIntoBullets();
                return;
            }

            if (owner.RemainingShots <= 0)
            {
                KeepMovingWhenOutOfAmmo();
                AvoidMovingIntoBullets();
                return;
            }

            float dist = Vector2.Distance(owner.Position, targetEnemy.Position);
            
            CheckIfStuck();

            bool hasLineOfSight = CanShootAtTarget();

            if (isInShootCooldown)
            {
                RepositionWhileCoolingDown(dist, hasLineOfSight);
                AvoidMovingTowardOwnBullets();
                AvoidMovingIntoBullets();
                return;
            }

            if (hasLineOfSight && dist < cellSize * 1.5f)
            {
                HandleCloseRangeEngagement();
            }
            else if (hasLineOfSight && dist < cellSize * 5f)
            {
                TryToShootOrAim();
            }
            else
            {
                MoveTowardTargetUsingPathfinding();
            }
            
            AvoidMovingTowardOwnBullets();
            AvoidMovingIntoBullets();
        }

        private void UpdateGridPosition()
        {
            currentGridIndex = WorldPosToGridIndex(owner.Position);
            currentGridCenter = GridIndexToWorldCenter(currentGridIndex);
        }

        private void SnapToGridCenter()
        {
            float distToCenter = Vector2.Distance(owner.Position, currentGridCenter);
            
            if (distToCenter > 0.1f)
            {
                desiredMoveVector = (currentGridCenter - owner.Position).normalized;
            }
            else
            {
                desiredMoveVector = Vector2.zero;
            }
        }

        private void MoveTowardTargetUsingPathfinding()
        {
            if (pathfinder == null || targetEnemy == null)
            {
                SnapToGridCenter();
                return;
            }
            
            if (Time.time - waypointCacheTime < 0.3f && cachedWaypoint != Vector2.zero)
            {
                float distToWaypoint = Vector2.Distance(owner.Position, cachedWaypoint);
                
                if (distToWaypoint > 0.2f && !IsWallNearby())
                {
                    desiredMoveVector = (cachedWaypoint - owner.Position).normalized;
                    return;
                }
            }
            
            Vector2 pathDirection = pathfinder.GetNextDirection(owner.Position, targetEnemy.Position);
            if (pathDirection.sqrMagnitude < 0.01f)
            {
                pathDirection = (targetEnemy.Position - owner.Position).normalized;
            }
            
            if (IsWallNearby())
            {
                cachedWaypoint = Vector2.zero;
                waypointCacheTime = 0;
            }
            else
            {
                cachedWaypoint = owner.Position + pathDirection * cellSize * 0.5f;
                waypointCacheTime = Time.time;
            }
            
            desiredMoveVector = pathDirection;
        }
        
        private void HandleCloseRangeEngagement()
        {
            if (targetEnemy == null) return;
            
            float dist = Vector2.Distance(owner.Position, targetEnemy.Position);
            bool hasLineOfSight = CanShootAtTarget();
            Vector2 toTarget = GetAimedDirectionToEnemy();
            Vector2 away = -toTarget;

            if (!hasLineOfSight)
            {
                MoveTowardTargetUsingPathfinding();
                return;
            }
            
            if (dist < cellSize * 0.4f)
            {
                Vector2 escapeDir = away;
                if (IsWallInDirection(away))
                {
                    Vector2 perp1 = Vector2.Perpendicular(away);
                    Vector2 perp2 = -perp1;
                    
                    if (!IsWallInDirection(perp1))
                        escapeDir = perp1;
                    else if (!IsWallInDirection(perp2))
                        escapeDir = perp2;
                    else
                        escapeDir = Random.insideUnitCircle.normalized;
                }
                desiredMoveVector = escapeDir;
                return;
            }
            
            if (stuckCounter > 4)
            {
                Vector2 randomDir = FindOpenDirection();
                desiredMoveVector = randomDir;
                stuckCounter = 0;
            }
            else if (owner.RemainingShots > 0)
            {
                Transform nozzle = owner.transform.Find("Nozzle");
                if (nozzle != null)
                {
                    Vector2 facingDir = nozzle.right;
                    float angle = Vector2.Angle(facingDir, toTarget);
                    
                    if (angle < 25f && Time.time - lastShootTime > 1.2f)
                    {
                        if (ShouldHesitateBeforeShooting())
                        {
                            Vector2 strafe = Vector2.Perpendicular(toTarget);
                            if (Random.value > 0.5f)
                            {
                                strafe = -strafe;
                            }

                            desiredMoveVector = (toTarget * 0.2f + strafe * 0.8f).normalized;
                        }
                        else
                        {
                            QueueShootTap();
                            desiredMoveVector = toTarget * 0.15f;
                        }
                    }
                    else if (angle < 60f)
                    {
                        desiredMoveVector = toTarget * 0.3f;
                    }
                    else
                    {
                        Vector2 strafeDir = Vector2.Perpendicular(toTarget);
                        if (Random.value > 0.5f) strafeDir = -strafeDir;
                        desiredMoveVector = (toTarget * 0.4f + strafeDir * 0.6f).normalized;
                    }
                }
                else
                {
                    desiredMoveVector = toTarget * 0.4f;
                }
            }
            else
            {
                if (IsWallInDirection(away))
                {
                    desiredMoveVector = FindOpenDirection();
                }
                else
                {
                    Vector2 escapeTarget = owner.Position + away * cellSize * 2.5f;
                    Vector2 pathDirection = pathfinder.GetNextDirection(owner.Position, escapeTarget);
                    desiredMoveVector = pathDirection;
                }
            }
        }
        
        private void CheckIfStuck()
        {
            if (Time.time - stuckCheckTime > 0.4f)
            {
                float distMoved = Vector2.Distance(owner.Position, lastPosition);
                
                if (distMoved < 0.08f && desiredMoveVector.magnitude > 0.1f)
                {
                    stuckCounter++;
                    cachedWaypoint = Vector2.zero;
                    waypointCacheTime = 0;
                    
                    if (stuckCounter > 2)
                    {
                        desiredMoveVector = FindOpenDirection();
                    }
                }
                else
                {
                    stuckCounter = Mathf.Max(0, stuckCounter - 1);
                }
                
                lastPosition = owner.Position;
                stuckCheckTime = Time.time;
            }
        }

        private void TryToShootOrAim()
        {
            Transform nozzle = owner.transform.Find("Nozzle");
            if (nozzle == null)
            {
                MoveTowardTargetUsingPathfinding();
                return;
            }

            Vector2 toTarget = GetAimedDirectionToEnemy();
            Vector2 facingDir = nozzle.right;
            float angle = Vector2.Angle(facingDir, toTarget);
            
            if (angle < 30f && Time.time - lastShootTime > 1.5f && CanShootAtTarget())
            {
                if (ShouldHesitateBeforeShooting())
                {
                    desiredMoveVector = Vector2.Perpendicular(toTarget) * (Random.value > 0.5f ? 1f : -1f);
                }
                else
                {
                    QueueShootTap();
                    desiredMoveVector = toTarget * 0.1f;
                }
            }
            else
            {
                if (Time.time - waypointCacheTime < 0.4f && cachedWaypoint != Vector2.zero && !IsWallNearby())
                {
                    float distToWaypoint = Vector2.Distance(owner.Position, cachedWaypoint);
                    
                    if (distToWaypoint > 0.2f)
                    {
                        desiredMoveVector = (cachedWaypoint - owner.Position).normalized * 0.8f;
                        return;
                    }
                }
                
                Vector2 pathDirection = pathfinder.GetNextDirection(owner.Position, targetEnemy.Position);
                
                if (IsWallNearby())
                {
                    cachedWaypoint = Vector2.zero;
                    waypointCacheTime = 0;
                }
                else
                {
                    cachedWaypoint = owner.Position + pathDirection * cellSize * 0.5f;
                    waypointCacheTime = Time.time;
                }
                
                desiredMoveVector = pathDirection * 0.8f;
            }
        }

        private int WorldPosToGridIndex(Vector2 worldPos)
        {
            Vector2 relative = worldPos - gridOrigin;
            int col = Mathf.RoundToInt((relative.x - cellSize * 0.5f) / cellSize);
            int row = Mathf.RoundToInt((relative.y - cellSize * 0.5f) / cellSize);
            
            col = Mathf.Clamp(col, 0, gridCols - 1);
            row = Mathf.Clamp(row, 0, gridRows - 1);
            
            return row * gridCols + col;
        }

        private Vector2 GridIndexToWorldCenter(int index)
        {
            int col = index % gridCols;
            int row = index / gridCols;
            
            return gridOrigin + new Vector2(col * cellSize + cellSize * 0.5f, row * cellSize + cellSize * 0.5f);
        }

        private void FindTarget()
        {
            TankBattlePlayer[] allPlayers = FindObjectsByType<TankBattlePlayer>(FindObjectsSortMode.None);
            TankBattlePlayer closest = null;
            float closestDist = float.MaxValue;

            foreach (TankBattlePlayer player in allPlayers)
            {
                if (player == owner || !player.IsAlive) continue;

                float dist = Vector2.Distance(owner.Position, player.Position);
                if (dist < closestDist)
                {
                    closest = player;
                    closestDist = dist;
                }
            }

            targetEnemy = closest;
        }

        private void KeepMovingWhenOutOfAmmo()
        {
            if (targetEnemy == null)
            {
                ExploreRandomly();
                return;
            }
            
            float distToEnemy = Vector2.Distance(owner.Position, targetEnemy.Position);
            
            if (distToEnemy < cellSize * 3f)
            {
                Vector2 bestEscape = owner.Position;
                
                for (int i = 0; i < 8; i++)
                {
                    int rCol = Random.Range(0, gridCols);
                    int rRow = Random.Range(0, gridRows);
                    Vector2 candidate = gridOrigin + new Vector2(rCol * cellSize + cellSize * 0.5f, rRow * cellSize + cellSize * 0.5f);
                    
                    float dToEnemy = Vector2.Distance(candidate, targetEnemy.Position);
                    float dToSelf = Vector2.Distance(candidate, owner.Position);
                    
                    if (dToEnemy > cellSize * 4f && dToSelf < cellSize * 8f)
                    {
                        Vector2 nextDir = pathfinder.GetNextDirection(owner.Position, candidate);
                        if (nextDir.magnitude > 0.1f) 
                        {
                            bestEscape = candidate;
                            break;
                        }
                    }
                }
                
                if (bestEscape != owner.Position)
                {
                    randomExploreTarget = bestEscape;
                    exploreTargetSetTime = Time.time;
                    desiredMoveVector = pathfinder.GetNextDirection(owner.Position, bestEscape);
                }
                else
                {
                    desiredMoveVector = FindOpenDirection();
                }
            }
            else
            {
                ExploreRandomly();
            }
        }
        
        private void ExploreRandomly()
        {
            if (Time.time - exploreTargetSetTime > 3f || Vector2.Distance(owner.Position, randomExploreTarget) < cellSize * 0.5f)
            {
                for (int i = 0; i < 10; i++)
                {
                    int randomCol = Random.Range(0, gridCols);
                    int randomRow = Random.Range(0, gridRows);
                    randomExploreTarget = gridOrigin + new Vector2(randomCol * cellSize + cellSize * 0.5f, randomRow * cellSize + cellSize * 0.5f);
                    
                    if (targetEnemy != null)
                    {
                        if (Vector2.Distance(randomExploreTarget, targetEnemy.Position) > cellSize * 3f)
                        {
                            break;
                        }
                    }
                }
                exploreTargetSetTime = Time.time;
            }
            
            Vector2 pathDirection = pathfinder.GetNextDirection(owner.Position, randomExploreTarget);
            desiredMoveVector = pathDirection;
        }

        private bool HandleBulletDodging()
        {
            if (Time.time < nextDodgeAllowedTime)
            {
                return false;
            }

            TankBattleBullet[] bullets = FindObjectsByType<TankBattleBullet>(FindObjectsSortMode.None);
            foreach (var bullet in bullets)
            {
                if (bullet.OwnerPlayer == owner.gameObject) continue;

                float dist = Vector2.Distance(owner.Position, bullet.transform.position);
                if (dist < cellSize * 2.35f)
                {
                    Vector2 bulletVelocity = GetBulletVelocity(bullet);
                    Vector2 toOwner = owner.Position - (Vector2)bullet.transform.position;

                    if (bulletVelocity.magnitude > 0.05f && toOwner.sqrMagnitude > 0.0001f)
                    {
                        float approachingDot = Vector2.Dot(bulletVelocity.normalized, toOwner.normalized);
                        float timeToReach = dist / Mathf.Max(0.001f, bulletVelocity.magnitude);

                        if (approachingDot > 0.55f && timeToReach < 0.75f)
                        {
                            Vector2 dodgeDir = Vector2.Perpendicular(bulletVelocity.normalized);
                            
                            Vector2 dodgePos1 = owner.Position + dodgeDir * cellSize * 0.5f;
                            Vector2 dodgePos2 = owner.Position - dodgeDir * cellSize * 0.5f;
                            
                            bool wall1 = IsWallInDirection(dodgeDir);
                            bool wall2 = IsWallInDirection(-dodgeDir);
                            
                            if (wall1 && !wall2) 
                            {
                                dodgeDir = -dodgeDir;
                            }
                            else if (!wall1 && !wall2)
                            {
                                if (targetEnemy != null)
                                {
                                    float d1 = Vector2.Distance(dodgePos1, targetEnemy.Position);
                                    float d2 = Vector2.Distance(dodgePos2, targetEnemy.Position);
                                    if (d1 <= d2) 
                                    {
                                        dodgeDir = -dodgeDir;
                                    }
                                }
                            }
                            else if (wall1 && wall2)
                            {
                                dodgeDir = -bulletVelocity.normalized;
                                if (IsWallInDirection(dodgeDir))
                                    dodgeDir = bulletVelocity.normalized;
                            }
                            
                            desiredMoveVector = dodgeDir.normalized;
                            nextDodgeAllowedTime = Time.time + 0.16f;
                            return true;
                        }
                    }
                }
            }
            return false;
        }

        private void AvoidMovingTowardOwnBullets()
        {
            if (desiredMoveVector.sqrMagnitude < 0.01f || owner == null)
            {
                return;
            }

            TankBattleBullet[] bullets = FindObjectsByType<TankBattleBullet>(FindObjectsSortMode.None);
            Vector2 moveDir = desiredMoveVector.normalized;
            Vector2 projectedPosition = owner.Position + moveDir * cellSize * 0.6f;

            for (int i = 0; i < bullets.Length; i++)
            {
                TankBattleBullet bullet = bullets[i];
                if (bullet == null || bullet.OwnerPlayer != owner.gameObject)
                {
                    continue;
                }

                Vector2 bulletPosition = bullet.transform.position;
                float currentDist = Vector2.Distance(owner.Position, bulletPosition);
                if (currentDist > cellSize * 2.4f)
                {
                    continue;
                }

                float nextDist = Vector2.Distance(projectedPosition, bulletPosition);
                if (nextDist >= currentDist)
                {
                    continue;
                }

                Vector2 away = (owner.Position - bulletPosition).normalized;
                if (away.sqrMagnitude < 0.0001f)
                {
                    away = Vector2.Perpendicular(moveDir).normalized;
                }

                Vector2 escape = away;
                if (IsWallInDirection(escape))
                {
                    Vector2 perpendicularA = Vector2.Perpendicular(away).normalized;
                    Vector2 perpendicularB = -perpendicularA;

                    if (!IsWallInDirection(perpendicularA))
                    {
                        escape = perpendicularA;
                    }
                    else if (!IsWallInDirection(perpendicularB))
                    {
                        escape = perpendicularB;
                    }
                    else
                    {
                        escape = Vector2.zero;
                    }
                }

                desiredMoveVector = escape;
                return;
            }
        }

        private void AvoidMovingIntoBullets()
        {
            if (desiredMoveVector.magnitude < 0.1f) return;
            
            TankBattleBullet[] bullets = FindObjectsByType<TankBattleBullet>(FindObjectsSortMode.None);
            Vector2 proposedDir = desiredMoveVector.normalized;
            Vector2 proposedPos = owner.Position + proposedDir * cellSize * 0.5f;
            
            foreach (var bullet in bullets)
            {
                float currentDist = Vector2.Distance(owner.Position, bullet.transform.position);
                float nextDist = Vector2.Distance(proposedPos, bullet.transform.position);
                
                if (nextDist < cellSize * 0.8f && nextDist < currentDist)
                {
                    Vector2 testDir1 = Vector2.Perpendicular(proposedDir);
                    Vector2 testDir2 = -testDir1;
                    
                    bool wall1 = IsWallInDirection(testDir1);
                    bool wall2 = IsWallInDirection(testDir2);
                    
                    if (!wall1 && wall2) desiredMoveVector = testDir1;
                    else if (wall1 && !wall2) desiredMoveVector = testDir2;
                    else if (!wall1 && !wall2)
                    {
                        float d1 = Vector2.Distance(owner.Position + testDir1, bullet.transform.position);
                        float d2 = Vector2.Distance(owner.Position + testDir2, bullet.transform.position);
                        desiredMoveVector = (d1 > d2) ? testDir1 : testDir2;
                    }
                    else
                    {
                        desiredMoveVector = Vector2.zero;
                    }
                    
                    break;
                }
            }
        }

        private bool CanShootAtTarget()
        {
            if (targetEnemy == null) return false;

            Vector2 castOrigin = owner.Position;
            Transform nozzle = owner.transform.Find("Nozzle");
            if (nozzle != null)
            {
                castOrigin = nozzle.position;
            }

            Vector2 toTarget = targetEnemy.Position - castOrigin;
            float dist = toTarget.magnitude;
            if (dist <= 0.001f)
            {
                return false;
            }

            float maxDist = settings != null ? settings.shootRaycastDistance : 15f;
            float rayDist = Mathf.Min(dist, maxDist);
            
            RaycastHit2D[] hits = Physics2D.RaycastAll(castOrigin, toTarget.normalized, rayDist);
            
            foreach (RaycastHit2D hit in hits)
            {
                if (hit.collider == null) continue;
                
                if (hit.collider.CompareTag("Wall") ||
                    hit.collider.name.Contains("Bdr") ||
                    hit.collider.name.Contains("HW") ||
                    hit.collider.name.Contains("VW"))
                {
                    return false;
                }
                
                TankBattlePlayer hitPlayer = hit.collider.GetComponent<TankBattlePlayer>();
                if (hitPlayer == targetEnemy)
                {
                    return true;
                }
            }
            
            return false;
        }
        
        private bool IsWallNearby()
        {
            float checkDist = cellSize * 0.3f;
            Vector2[] directions = new Vector2[]
            {
                desiredMoveVector.normalized,
                Vector2.Perpendicular(desiredMoveVector.normalized),
                -Vector2.Perpendicular(desiredMoveVector.normalized)
            };
            
            foreach (Vector2 dir in directions)
            {
                if (dir.magnitude < 0.1f) continue;
                RaycastHit2D hit = Physics2D.Raycast(owner.Position, dir, checkDist);
                if (hit.collider != null && (hit.collider.CompareTag("Wall") || 
                    hit.collider.name.Contains("Bdr") || 
                    hit.collider.name.Contains("HW") || 
                    hit.collider.name.Contains("VW")))
                {
                    return true;
                }
            }
            
            return false;
        }
        
        private bool IsWallInDirection(Vector2 direction)
        {
            if (direction.magnitude < 0.1f) return false;
            
            float checkDist = cellSize * 0.4f;
            RaycastHit2D hit = Physics2D.Raycast(owner.Position, direction.normalized, checkDist);
            
            if (hit.collider != null && (hit.collider.CompareTag("Wall") || 
                hit.collider.name.Contains("Bdr") || 
                hit.collider.name.Contains("HW") || 
                hit.collider.name.Contains("VW")))
            {
                return true;
            }
            
            return false;
        }
        
        private Vector2 FindOpenDirection()
        {
            Vector2[] testDirections = new Vector2[]
            {
                Vector2.right, Vector2.left, Vector2.up, Vector2.down,
                new Vector2(1, 1).normalized, new Vector2(-1, 1).normalized,
                new Vector2(1, -1).normalized, new Vector2(-1, -1).normalized
            };
            
            foreach (Vector2 dir in testDirections)
            {
                if (!IsWallInDirection(dir))
                {
                    return dir;
                }
            }
            
            return Random.insideUnitCircle.normalized;
        }

        private void SmoothMovement(float deltaTime)
        {
            float smoothness = settings != null ? settings.steeringSmoothness : 8f;
            currentMoveVector = Vector2.Lerp(currentMoveVector, desiredMoveVector, deltaTime * smoothness);

            if (currentMoveVector.sqrMagnitude < 0.0025f)
            {
                currentMoveVector = Vector2.zero;
            }
            else if (currentMoveVector.sqrMagnitude > 1f)
            {
                currentMoveVector.Normalize();
            }
        }

        private void ScheduleNextDecision()
        {
            float interval = settings != null ? settings.decisionInterval : 0.2f;
            float jitter = settings != null ? settings.decisionJitter : 0.1f;
            float jitterOffset = Random.Range(-Mathf.Max(0f, jitter), Mathf.Max(0f, jitter));
            nextDecisionTime = Time.time + Mathf.Max(0.06f, interval + jitterOffset);
        }

        private void QueueShootTap()
        {
            float tapDuration = settings != null ? settings.shootTapHoldSeconds : 0.08f;
            tapDuration = Mathf.Max(0.02f, tapDuration);
            shootTapEndTime = Mathf.Max(shootTapEndTime, Time.time + tapDuration);
            lastShootTime = Time.time;
            shootPauseEndTime = Time.time + Random.Range(0.45f, 0.75f);
        }

        private Vector2 GetAimedDirectionToEnemy()
        {
            if (targetEnemy == null)
            {
                return Vector2.right;
            }

            Vector2 baseDirection = (targetEnemy.Position - owner.Position).normalized;
            if (baseDirection.sqrMagnitude <= 0.0001f)
            {
                return Vector2.right;
            }

            float maxAimError = settings != null ? settings.aimErrorDegrees : 6f;
            if (maxAimError <= 0.01f)
            {
                return baseDirection;
            }

            float error = Random.Range(-maxAimError, maxAimError);
            return Rotate(baseDirection, error);
        }

        private bool ShouldHesitateBeforeShooting()
        {
            float chance = settings != null ? settings.hesitationChance : 0.15f;
            return chance > 0f && Random.value < chance;
        }

        private static Vector2 Rotate(Vector2 direction, float degrees)
        {
            float radians = degrees * Mathf.Deg2Rad;
            float sin = Mathf.Sin(radians);
            float cos = Mathf.Cos(radians);
            return new Vector2(
                direction.x * cos - direction.y * sin,
                direction.x * sin + direction.y * cos);
        }

        private void RepositionWhileCoolingDown(float dist, bool hasLineOfSight)
        {
            if (targetEnemy == null)
            {
                desiredMoveVector = Vector2.zero;
                return;
            }

            Vector2 toEnemy = (targetEnemy.Position - owner.Position).normalized;
            if (toEnemy.sqrMagnitude <= 0.0001f)
            {
                desiredMoveVector = Vector2.zero;
                return;
            }

            if (!hasLineOfSight)
            {
                MoveTowardTargetUsingPathfinding();
                return;
            }

            if (dist > cellSize * 3f)
            {
                desiredMoveVector = toEnemy;
                return;
            }

            Vector2 strafe = Vector2.Perpendicular(toEnemy);
            if (Random.value > 0.5f)
            {
                strafe = -strafe;
            }

            if (IsWallInDirection(strafe))
            {
                strafe = -strafe;
            }

            desiredMoveVector = (toEnemy * 0.45f + strafe * 0.55f).normalized;
        }

        private static Vector2 GetBulletVelocity(TankBattleBullet bullet)
        {
            if (bullet == null)
            {
                return Vector2.zero;
            }

            Vector2 velocity = bullet.CurrentVelocity;
            if (velocity.sqrMagnitude > 0.0025f)
            {
                return velocity;
            }

            Rigidbody2D rb = bullet.GetComponent<Rigidbody2D>();
            return rb != null ? rb.linearVelocity : velocity;
        }
    }
}
