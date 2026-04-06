using UnityEngine;
using System.Collections.Generic;

namespace TankBattle
{
    public class TankBattlePathfinder
    {
        private class Node
        {
            public Vector2Int gridPos;
            public Vector2 worldPos;
            public float gCost;
            public float hCost;
            public float fCost => gCost + hCost;
            public Node parent;

            public Node(Vector2Int pos, Vector2 world)
            {
                gridPos = pos;
                worldPos = world;
            }
        }

        private float cellSize;
        private Vector2 gridOrigin;
        private int gridWidth;
        private int gridHeight;

        public TankBattlePathfinder(float cellSize, Vector2 origin, int width, int height)
        {
            this.cellSize = cellSize;
            this.gridOrigin = origin;
            this.gridWidth = width;
            this.gridHeight = height;
        }

        public Vector2 GetNextDirection(Vector2 startPos, Vector2 targetPos)
        {
            Vector2Int startGrid = WorldToGrid(startPos);
            Vector2Int targetGrid = WorldToGrid(targetPos);
            
            if (startGrid == targetGrid)
            {
                return (targetPos - startPos).normalized;
            }
            float directDistance = Vector2.Distance(startPos, targetPos);
            if (directDistance < cellSize * 0.8f && !IsWallBetween(startPos, targetPos))
            {
                return (targetPos - startPos).normalized;
            }

            List<Node> path = FindPath(startGrid, targetGrid, startPos);

            if (path != null && path.Count > 0)
            {
                Vector2 nextWaypoint = path.Count > 1 ? path[1].worldPos : path[0].worldPos;
                Vector2 direction = (nextWaypoint - startPos).normalized;
                
                return direction;
            }

            return (targetPos - startPos).normalized;
        }

        private List<Node> FindPath(Vector2Int start, Vector2Int target, Vector2 startWorldPos)
        {
            List<Node> openSet = new List<Node>();
            HashSet<Vector2Int> closedSet = new HashSet<Vector2Int>();
            
            Node startNode = new Node(start, startWorldPos);
            startNode.gCost = 0;
            startNode.hCost = GetDistance(start, target);
            
            openSet.Add(startNode);

            int maxIterations = 200;
            int iterations = 0;
            
            Node closestNode = startNode;
            float closestDistance = startNode.hCost;

            while (openSet.Count > 0 && iterations < maxIterations)
            {
                iterations++;
                
                Node currentNode = openSet[0];
                for (int i = 1; i < openSet.Count; i++)
                {
                    if (openSet[i].fCost < currentNode.fCost || 
                        (openSet[i].fCost == currentNode.fCost && openSet[i].hCost < currentNode.hCost))
                    {
                        currentNode = openSet[i];
                    }
                }

                openSet.Remove(currentNode);
                closedSet.Add(currentNode.gridPos);

                if (currentNode.hCost < closestDistance)
                {
                    closestDistance = currentNode.hCost;
                    closestNode = currentNode;
                }

                if (currentNode.gridPos == target)
                {
                    return RetracePath(startNode, currentNode);
                }

                foreach (Node neighbor in GetNeighbors(currentNode))
                {
                    if (closedSet.Contains(neighbor.gridPos))
                    {
                        continue;
                    }

                    float newGCost = currentNode.gCost + GetDistance(currentNode.gridPos, neighbor.gridPos);

                    Node existingNode = openSet.Find(n => n.gridPos == neighbor.gridPos);
                    
                    if (existingNode == null)
                    {
                        neighbor.gCost = newGCost;
                        neighbor.hCost = GetDistance(neighbor.gridPos, target);
                        neighbor.parent = currentNode;
                        openSet.Add(neighbor);
                    }
                    else if (newGCost < existingNode.gCost)
                    {
                        existingNode.gCost = newGCost;
                        existingNode.parent = currentNode;
                    }
                }
            }

            if (closestNode != startNode)
            {
                return RetracePath(startNode, closestNode);
            }

            return null;
        }

        private List<Node> RetracePath(Node startNode, Node endNode)
        {
            List<Node> path = new List<Node>();
            Node currentNode = endNode;

            while (currentNode != startNode)
            {
                path.Add(currentNode);
                currentNode = currentNode.parent;
                
                if (currentNode == null)
                    break;
            }

            path.Reverse();
            return path;
        }

        private List<Node> GetNeighbors(Node node)
        {
            List<Node> neighbors = new List<Node>();
            
            Vector2Int[] directions = new Vector2Int[]
            {
                new Vector2Int(1, 0),
                new Vector2Int(-1, 0),
                new Vector2Int(0, 1),
                new Vector2Int(0, -1),
                new Vector2Int(1, 1),
                new Vector2Int(-1, 1),
                new Vector2Int(1, -1),
                new Vector2Int(-1, -1)
            };

            foreach (Vector2Int dir in directions)
            {
                Vector2Int neighborPos = node.gridPos + dir;
                
                if (neighborPos.x >= 0 && neighborPos.x < gridWidth && 
                    neighborPos.y >= 0 && neighborPos.y < gridHeight)
                {
                    Vector2 worldPos = GridToWorld(neighborPos);
                    
                    if (!IsWallBetween(node.worldPos, worldPos))
                    {
                        neighbors.Add(new Node(neighborPos, worldPos));
                    }
                }
            }

            return neighbors;
        }

        private bool IsWallBetween(Vector2 start, Vector2 end)
        {
            float checkRadius = cellSize * 0.25f;
            Vector2 dir = (end - start).normalized;
            float dist = Vector2.Distance(start, end);
            
            RaycastHit2D[] hits = Physics2D.CircleCastAll(start, checkRadius, dir, dist);
            
            foreach (RaycastHit2D hit in hits)
            {
                if (hit.collider == null) continue;
                
                if (hit.collider.CompareTag("Wall") ||
                    hit.collider.name.Contains("Bdr") ||
                    hit.collider.name.Contains("HW") ||
                    hit.collider.name.Contains("VW"))
                {
                    return true;
                }
            }
            
            return false;
        }

        private float GetDistance(Vector2Int a, Vector2Int b)
        {
            int dx = Mathf.Abs(a.x - b.x);
            int dy = Mathf.Abs(a.y - b.y);
            
            if (dx > dy)
                return 1.4f * dy + (dx - dy);
            return 1.4f * dx + (dy - dx);
        }

        private Vector2Int WorldToGrid(Vector2 worldPos)
        {
            Vector2 relative = worldPos - gridOrigin;
            int x = Mathf.Clamp(Mathf.RoundToInt(relative.x / cellSize), 0, gridWidth - 1);
            int y = Mathf.Clamp(Mathf.RoundToInt(relative.y / cellSize), 0, gridHeight - 1);
            
            return new Vector2Int(x, y);
        }

        private Vector2 GridToWorld(Vector2Int gridPos)
        {
            return gridOrigin + new Vector2(gridPos.x * cellSize + cellSize * 0.5f, gridPos.y * cellSize + cellSize * 0.5f);
        }
    }
}
