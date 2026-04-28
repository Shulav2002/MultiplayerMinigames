using UnityEngine;
using UnityEngine.Tilemaps;
using TankBattle;

/// <summary>
/// Tank-Battle procedural map generator.
///
/// SPAWN FIX    : GenerateMap() is called by TankBattleGameManager â€” NOT from Start().
///                This removes Unity Start() ordering race conditions.
/// WALL ALGO    : Wall count and length are derived from grid size automatically.
/// SORTING LAYER: Wall segments use the "Wall" sorting layer.
/// SHADER       : Uses sprite with outline shader instead of fake quads.
/// </summary>
public class TankBattleMapGenerator : MonoBehaviour
{
    [Header("Tilemap (floor only)")]
    public Tilemap  floorTilemap;
    public TileBase floorTile;

    [Header("Camera")]
    public Camera mapCamera;
    public float  screenPadding = 0.2f;

    [Header("Fixed Cell & Gap Size")]
    [Tooltip("Cell width/height in world-units (same regardless of column count)")]
    public float cellSize = 1.0f;
    [Tooltip("Gap between cells â€” used for grid lines, walls, AND border")]
    public float gapSize  = 0.12f;

    [Header("Columns (random per round)")]
    public int minCols = 6;
    public int maxCols = 9;

    [Header("Rows (random per round, capped at cols)")]
    public int minRows = 4;
    public int maxRows = 9;

    [Header("Color Themes (one entry = one theme, chosen randomly each round)")]

    [Tooltip("Tint for the floor tiles")]
    public Color[] gridColors = new Color[]
    {
        HexColor("739DC8"),   // blue
        HexColor("5C80A4"),   // dark blue
        HexColor("AAAAAA"),   // gray
    };
    [Tooltip("Background quad color â€” visible through tile gaps")]
    public Color[] gapColors  = new Color[]
    {
        HexColor("7098C2"),
        HexColor("B78654"),
        HexColor("A5A5A5"),
    };
    [Tooltip("Wall and border segment color")]
    public Color[] wallColors = new Color[]
    {
        HexColor("5C80A4"),
        HexColor("91663B"),
        HexColor("828282"),
    };
    [Tooltip("Outline/stroke color drawn around each wall (via shader)")]
    public Color[] outlineColors = new Color[]
    {
        HexColor("000000"),   // darker blue
        HexColor("000000"),   // darker brown
        HexColor("000000"),   // dark gray
    };

    [Header("Wall Outline")]
    [Tooltip("Outline width in world-units added around every wall/border segment")]
    public float outlineThickness = 0.04f;

    [Header("Seed")]
    public int  seed = 0;
    public bool randomSeedEveryRound = true;

    [Header("Global Config")]
    [Tooltip("If enabled and a TankBattleGlobalConfig exists, sorting values are pulled from it.")]
    [SerializeField] private bool useGlobalSortingConfig = true;
    [SerializeField] private TankBattleGlobalConfig globalConfig;

    [Header("Sorting")]
    [Tooltip("Sorting layer for floor visuals (tilemap + gap background).")]
    public string floorSortingLayerName = "Default";
    [Tooltip("Sorting layer for generated walls.")]
    public string wallSortingLayerName = "Default";
    [Tooltip("Floor tilemap sorting order (recommended 0).")]
    public int floorTilemapSortingOrder = 0;
    [Tooltip("Gap background sorting order (keep below floor tilemap; recommended -1).")]
    public int floorBackgroundSortingOrder = -1;
    [Tooltip("Generated wall fill sorting order (recommended 1).")]
    public int wallSortingOrder = 1;
    [Tooltip("Generated wall outline sorting order (recommended 0).")]
    public int wallOutlineSortingOrder = 0;

    // â”€â”€ Runtime â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    [HideInInspector] public int  lastCols, lastRows;
    float cs, gap;
    Color activeWallColor;
    Color activeOutlineColor;
    bool[,] hEdge, vEdge;
    Transform wallParent;
    GameObject arenaBackground;
    Sprite    _pxSprite;
    Vector2Int p1Cell, p2Cell;
    Vector3    gridOrigin;

    // Helper â€” parse a hex string like "739DC8" into a Unity Color
    static Color HexColor(string hex)
    {
        if (UnityEngine.ColorUtility.TryParseHtmlString("#" + hex, out Color c)) return c;
        return Color.white;
    }

    // â”€â”€ Public API â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    public Vector3 GetPlayer1Spawn() => CellCentre(p1Cell);
    public Vector3 GetPlayer2Spawn() => CellCentre(p2Cell);

    // NOTE: Do NOT auto-generate in Start().
    // TankBattleGameManager calls GenerateMap() explicitly so spawning
    // always happens AFTER the map is ready.

    public void GenerateMap()
    {
        ApplyGlobalSortingIfEnabled();

        if (randomSeedEveryRound) seed = Random.Range(0, 999999);
        Random.InitState(seed);

        // â”€â”€ 0. Pick a random color theme â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        int themeCount = Mathf.Min(
            gridColors != null ? gridColors.Length : 1,
            Mathf.Min(
                gapColors  != null ? gapColors.Length  : 1,
                wallColors != null ? wallColors.Length : 1));
        themeCount = Mathf.Max(1, themeCount);
        int themeIdx = Random.Range(0, themeCount);

        Color activeGridColor = (gridColors != null && gridColors.Length > 0) ? gridColors[themeIdx] : Color.white;
        Color activeGapColor  = (gapColors  != null && gapColors.Length  > 0) ? gapColors[themeIdx]  : new Color(0.48f, 0.55f, 0.60f);
        activeWallColor    = (wallColors    != null && wallColors.Length    > 0) ? wallColors[themeIdx]    : Color.black;
        activeOutlineColor = (outlineColors != null && outlineColors.Length > 0) ? outlineColors[themeIdx] : new Color(0f,0f,0f,0.5f);

        cs  = cellSize;
        gap = gapSize;

        // â”€â”€ 1. Choose grid dimensions â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        lastCols = Random.Range(Mathf.Max(2, minCols), Mathf.Max(3, maxCols) + 1);

        float camH   = mapCamera != null ? mapCamera.orthographicSize * 2f : 10f;
        float availH = camH - screenPadding * 2f;

        int maxRowsFit = Mathf.FloorToInt(availH / (cs + gap));
        int rMin = Mathf.Max(2, Mathf.Min(minRows, lastCols));
        int rMax = Mathf.Min(Mathf.Min(maxRows, lastCols), maxRowsFit);
        lastRows = Random.Range(rMin, Mathf.Max(rMin, rMax) + 1);

        // local shorthands
        int cols = lastCols, rows = lastRows;

        // Apply floor tile colour
        floorTilemap.color = activeGridColor;
        ApplyFloorTilemapSorting();


        // â”€â”€ 2. Grid component sizes â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        Grid g = GetComponentInParent<Grid>();
        if (g == null) g = GetComponent<Grid>();
        if (g != null) { g.cellSize = new Vector3(cs, cs, 0f); g.cellGap = new Vector3(gap, gap, 0f); }

        // NOTE: Camera background colour is intentionally NOT set here.
        // The arena uses a background quad for grid colour; leave camera colour to the user.

        // â”€â”€ 3. Clear old content â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        floorTilemap.ClearAllTiles();
        if (wallParent       != null) Destroy(wallParent.gameObject);
        if (arenaBackground  != null) Destroy(arenaBackground);
        wallParent = new GameObject("_Walls").transform;
        wallParent.SetParent(transform, false);

        hEdge = new bool[cols, rows + 1];
        vEdge = new bool[cols + 1, rows];

        // â”€â”€ 4. Compute inner area and centre the grid â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        float innerW = cols * (cs + gap) - gap;
        float innerH = rows * (cs + gap) - gap;

        // Move the TILEMAP (not Grid) to centre the tiles.
        // The Grid stays at origin; offset the floorTilemap's parent (this transform).
        transform.position = new Vector3(-innerW * 0.5f, -innerH * 0.5f, 0f);
        gridOrigin = transform.position;   // world-space bottom-left of first tile

        // Calculate cx0 and cy0 here as they are needed for the arena background
        float cx0    = gridOrigin.x + innerW * 0.5f;
        float cy0    = gridOrigin.y + innerH * 0.5f;

        // â”€â”€ 5. Arena background quad (grid gap colour, arena area only) â”€â”€
        // Sits at sorting order -2, behind floor tiles.
        // The floor tiles (white) sit on top; gridColor shows through
        // the cell gaps â€” and ONLY inside the arena, not the whole camera.
        arenaBackground = new GameObject("_ArenaBG");
        arenaBackground.transform.SetParent(wallParent.parent, false);
        arenaBackground.transform.position   = new Vector3(cx0, cy0, 0f);

        // Size: inner area. The gap on each tile edge is already inside innerW/innerH.
        // Add one gap on each side so the colour fills flush to the border inner face.
        float bgW = innerW + gap;
        float bgH = innerH + gap;
        arenaBackground.transform.localScale  = new Vector3(bgW, bgH, 1f);

        SpriteRenderer bgSr = arenaBackground.AddComponent<SpriteRenderer>();
        bgSr.sprite           = GetPxSprite();
        bgSr.color            = activeGapColor;
        bgSr.sortingLayerName = floorSortingLayerName;
        bgSr.sortingOrder     = floorBackgroundSortingOrder;


        // 6. Floor tiles â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        for (int x = 0; x < cols; x++)
        for (int y = 0; y < rows; y++)
            floorTilemap.SetTile(new Vector3Int(x, y, 0), floorTile);

        // 7. Border â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        float outerW = innerW + gap * 2f;
        float outerH = innerH + gap * 2f;

        MakeSeg("Bdr_B", cx0, gridOrigin.y - gap * 0.5f,        outerW, gap,    wallSortingLayerName, wallSortingOrder);
        MakeSeg("Bdr_T", cx0, gridOrigin.y + innerH + gap*0.5f, outerW, gap,    wallSortingLayerName, wallSortingOrder);
        MakeSeg("Bdr_L", gridOrigin.x - gap * 0.5f,        cy0,         gap, outerH, wallSortingLayerName, wallSortingOrder);
        MakeSeg("Bdr_R", gridOrigin.x + innerW + gap * 0.5f, cy0,       gap, outerH, wallSortingLayerName, wallSortingOrder);

        // Mark border edges as occupied
        for (int x = 0; x < cols; x++) { hEdge[x, 0] = true; hEdge[x, rows] = true; }
        for (int y = 0; y < rows; y++) { vEdge[0, y]  = true; vEdge[cols, y] = true; }

        // â”€â”€ 7. Spawn cells (opposite corners, always clear) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        p1Cell = new Vector2Int(0,        0       );
        p2Cell = new Vector2Int(cols - 1, rows - 1);

        // â”€â”€ 8. Algorithmic wall generation â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        int attempt = 0;
        do {
            // Clear edge arrays (keep border marks)
            hEdge = new bool[cols, rows + 1];
            vEdge = new bool[cols + 1, rows];
            for (int x = 0; x < cols; x++) { hEdge[x, 0] = true; hEdge[x, rows] = true; }
            for (int y = 0; y < rows; y++) { vEdge[0, y]  = true; vEdge[cols, y] = true; }

            Random.InitState(seed + attempt);
            PlaceWalls(cols, rows);
            attempt++;
        } while (!AllCellsConnected(cols, rows) && attempt < 20);


        // â”€â”€ 9. Render walls â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        RenderEdges(cols, rows);

        // â”€â”€ 10. Camera centre â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        if (mapCamera != null)
            mapCamera.transform.position = new Vector3(0f, 0f, mapCamera.transform.position.z);
    }

    // â”€â”€ Algorithmic Wall Placement â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    //
    // Rules:
    //  â€¢ wallCount  = (cols + rows) Ã— 0.6  (scales with perimeter)
    //  â€¢ maxLength  = min(cols,rows) / 2    (can't reach the far wall)
    //  â€¢ GAP RULE: a stub stops one cell early if the NEXT cell would be
    //    immediately adjacent to an existing wall in the same direction
    //    â†’ always leaves â‰¥1 empty cell between any two tips
    //  â€¢ BRANCH: 40% chance to add a short perpendicular arm at the tip

    void PlaceWalls(int cols, int rows)
    {
        int wallCount = Mathf.RoundToInt((cols + rows) * 0.6f);
        int maxLen    = Mathf.Max(2, Mathf.Min(cols, rows) / 2);
        int guard     = 1;

        for (int p = 0; p < wallCount; p++)
        {
            int side   = Random.Range(0, 4);
            int length = Random.Range(2, maxLen + 1);
            int placed = 0;

            if (side == 0)   // â”€â”€ bottom â†’ vertical stub going UP â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
            {
                int cx = Random.Range(guard + 1, cols - guard);
                for (int i = 0; i < length; i++)
                {
                    int ry = i;
                    if (ry >= rows - guard) break;
                    // GAP RULE: stop if the cell ABOVE already has a vEdge (would leave 0 gap)
                    if (ry + 1 < rows && vEdge[cx, ry + 1]) break;
                    if (!TryVEdgeG(cx, ry, cols, rows)) break;
                    placed++;
                }
                if (placed > 0 && Random.value < 0.4f)
                    Branch_H(cx, placed - 1, cols, rows);
            }
            else if (side == 1)   // â”€â”€ top â†’ vertical stub going DOWN â”€â”€â”€â”€â”€â”€
            {
                int cx = Random.Range(guard + 1, cols - guard);
                for (int i = 0; i < length; i++)
                {
                    int ry = rows - 1 - i;
                    if (ry < guard) break;
                    // GAP RULE: stop if the cell BELOW already has a vEdge
                    if (ry - 1 >= 0 && vEdge[cx, ry - 1]) break;
                    if (!TryVEdgeG(cx, ry, cols, rows)) break;
                    placed++;
                }
                if (placed > 0 && Random.value < 0.4f)
                    Branch_H(cx, rows - placed, cols, rows);
            }
            else if (side == 2)   // â”€â”€ left â†’ horizontal stub going RIGHT â”€â”€
            {
                int cy = Random.Range(guard + 1, rows - guard);
                for (int i = 0; i < length; i++)
                {
                    int rx = i;
                    if (rx >= cols - guard) break;
                    // GAP RULE: stop if the cell to the RIGHT already has an hEdge
                    if (rx + 1 < cols && hEdge[rx + 1, cy]) break;
                    if (!TryHEdgeG(rx, cy, cols, rows)) break;
                    placed++;
                }
                if (placed > 0 && Random.value < 0.4f)
                    Branch_V(placed - 1, cy, cols, rows);
            }
            else                  // â”€â”€ right â†’ horizontal stub going LEFT â”€â”€
            {
                int cy = Random.Range(guard + 1, rows - guard);
                for (int i = 0; i < length; i++)
                {
                    int rx = cols - 1 - i;
                    if (rx < guard) break;
                    // GAP RULE: stop if the cell to the LEFT already has an hEdge
                    if (rx - 1 >= 0 && hEdge[rx - 1, cy]) break;
                    if (!TryHEdgeG(rx, cy, cols, rows)) break;
                    placed++;
                }
                if (placed > 0 && Random.value < 0.4f)
                    Branch_V(cols - placed, cy, cols, rows);
            }
        }
    }

    // â”€â”€ Branch helpers (perpendicular arm at tip) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    // Branches use CanPlaceH/CanPlaceV which adds:
    //   â€¢ parallel-gap check (same as main stems)
    //   â€¢ enclosure check: stops if placing would seal off a cell

    void Branch_H(int cx, int tipY, int cols, int rows)
    {
        bool goRight = Random.value > 0.5f;
        int  bLen    = Random.Range(1, 2);   // short: max 2 cells
        for (int b = 0; b < bLen; b++)
        {
            int bx = goRight ? cx + b : cx - 1 - b;
            if (!CanPlaceH(bx, tipY + 1, cols, rows)) break;
            hEdge[bx, tipY + 1] = true;
        }
    }

    void Branch_V(int tipX, int cy, int cols, int rows)
    {
        bool goUp = Random.value > 0.5f;
        int  bLen = Random.Range(1, 2);
        for (int b = 0; b < bLen; b++)
        {
            int by = goUp ? cy + b : cy - 1 - b;
            if (!CanPlaceV(tipX + 1, by, cols, rows)) break;
            vEdge[tipX + 1, by] = true;
        }
    }

    // â”€â”€ Edge placement with gap enforcement â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    // CanPlaceH: checks bounds, not already set, parallel-gap (INCLUDING border), enclosure
    bool CanPlaceH(int x, int y, int cols, int rows)
    {
        if (x < 0 || x >= cols || y <= 0 || y >= rows) return false;
        if (hEdge[x, y]) return false;
        // Parallel gap: must be >=1 row from any other hEdge INCLUDING the border (y=0 and y=rows)
        if (y - 1 >= 0    && hEdge[x, y - 1]) return false;   // was: y-1 > 0  (missed bottom border)
        if (y + 1 <= rows && hEdge[x, y + 1]) return false;   // was: y+1 < rows (missed top border)
        // Enclosure: placing hEdge[x,y] is TOP of cell (x, y-1)
        // Reject if left+right+bottom of that cell already set
        if (hEdge[x, y - 1] && vEdge[x, y - 1] && vEdge[x + 1, y - 1]) return false;
        // Placing hEdge[x,y] is BOTTOM of cell (x, y)
        // Reject if left+right+top of that cell already set
        if (y + 1 <= rows && hEdge[x, y + 1] && vEdge[x, y] && vEdge[x + 1, y]) return false;
        return true;
    }

    bool CanPlaceV(int x, int y, int cols, int rows)
    {
        if (x <= 0 || x >= cols || y < 0 || y >= rows) return false;
        if (vEdge[x, y]) return false;
        // Parallel gap: must be >=1 col from any other vEdge INCLUDING the border (x=0 and x=cols)
        if (x - 1 >= 0    && vEdge[x - 1, y]) return false;   // was: x-1 > 0  (missed left border)
        if (x + 1 <= cols && vEdge[x + 1, y]) return false;   // was: x+1 < cols (missed right border)
        // Enclosure: placing vEdge[x,y] is RIGHT of cell (x-1, y)
        // Reject if left+top+bottom of that cell already set
        if (vEdge[x - 1, y] && hEdge[x - 1, y] && hEdge[x - 1, y + 1]) return false;
        // Placing vEdge[x,y] is LEFT of cell (x, y)
        // Reject if right+top+bottom of that cell already set
        if (x + 1 <= cols && vEdge[x + 1, y] && hEdge[x, y] && hEdge[x, y + 1]) return false;
        return true;
    }

    // Main-stem versions (used by PlaceWalls â€” same as CanPlace but auto-writes)
    bool TryHEdgeG(int x, int y, int cols, int rows)
    {
        if (!CanPlaceH(x, y, cols, rows)) return false;
        hEdge[x, y] = true; return true;
    }

    bool TryVEdgeG(int x, int y, int cols, int rows)
    {
        if (!CanPlaceV(x, y, cols, rows)) return false;
        vEdge[x, y] = true; return true;
    }

    // Legacy no-gap-check versions used only for border marking
    bool TryHEdge(int x, int y, int cols, int rows)
    {
        if (x < 0 || x >= cols || y <= 0 || y >= rows) return false;
        if (hEdge[x, y]) return false;
        hEdge[x, y] = true; return true;
    }
    bool TryVEdge(int x, int y, int cols, int rows)
    {
        if (x <= 0 || x >= cols || y < 0 || y >= rows) return false;
        if (vEdge[x, y]) return false;
        vEdge[x, y] = true; return true;
    }


    // â”€â”€ BFS connectivity â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    // Returns true ONLY if ALL cells are reachable from p1Cell.
    // This catches isolated pockets (not just p1â†’p2 disconnection).
    bool AllCellsConnected(int cols, int rows)
    {
        bool[,] vis = new bool[cols, rows];
        var q = new System.Collections.Generic.Queue<Vector2Int>();
        q.Enqueue(p1Cell);
        vis[p1Cell.x, p1Cell.y] = true;
        int reached = 1;

        while (q.Count > 0)
        {
            var cur = q.Dequeue();
            int x = cur.x, y = cur.y;

            if (x+1 < cols && !vEdge[x+1, y] && !vis[x+1, y]) { vis[x+1,y]=true; reached++; q.Enqueue(new Vector2Int(x+1,y)); }
            if (x-1 >= 0   && !vEdge[x,   y] && !vis[x-1, y]) { vis[x-1,y]=true; reached++; q.Enqueue(new Vector2Int(x-1,y)); }
            if (y+1 < rows && !hEdge[x, y+1] && !vis[x, y+1]) { vis[x,y+1]=true; reached++; q.Enqueue(new Vector2Int(x,y+1)); }
            if (y-1 >= 0   && !hEdge[x,   y] && !vis[x, y-1]) { vis[x,y-1]=true; reached++; q.Enqueue(new Vector2Int(x,y-1)); }
        }
        return reached == cols * rows;   // every cell must be reachable
    }



    // â”€â”€ Render edges (merged runs â†’ continuous lines) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    void RenderEdges(int cols, int rows)
    {
        // Horizontal wall runs
        for (int y = 1; y < rows; y++)
        {
            int x = 0;
            while (x < cols)
            {
                if (!hEdge[x, y]) { x++; continue; }
                int s = x;
                while (x < cols && hEdge[x, y]) x++;
                float w  = (x - s) * cs + (x - s - 1) * gap;
                float wx = gridOrigin.x + s * (cs + gap) + w * 0.5f;
                float wy = gridOrigin.y + y * (cs + gap) - gap * 0.5f;
                MakeSeg("HW", wx, wy, w, gap, wallSortingLayerName, wallSortingOrder);
            }
        }

        // Vertical wall runs
        for (int x = 1; x < cols; x++)
        {
            int y = 0;
            while (y < rows)
            {
                if (!vEdge[x, y]) { y++; continue; }
                int s = y;
                while (y < rows && vEdge[x, y]) y++;
                float h  = (y - s) * cs + (y - s - 1) * gap;
                float wx = gridOrigin.x + x * (cs + gap) - gap * 0.5f;
                float wy = gridOrigin.y + s * (cs + gap) + h * 0.5f;
                MakeSeg("VW", wx, wy, gap, h, wallSortingLayerName, wallSortingOrder);
            }
        }

        // â”€â”€ L-junction coverage (wall endpoints meeting at right angles) â”€â”€
        // Only add small covers where walls actually meet to hide outline gaps
        for (int x = 1; x < cols; x++)
        for (int y = 1; y < rows; y++)
        {
            // Check all 4 possible L-junction configurations at this grid point
            bool vAbove = vEdge[x, y];
            bool vBelow = (y > 0 && vEdge[x, y - 1]);
            bool hLeft = hEdge[x - 1, y];
            bool hRight = hEdge[x, y];
            
            // Only place cover if exactly 2 perpendicular walls meet (L-shape)
            int wallCount = (vAbove ? 1 : 0) + (vBelow ? 1 : 0) + (hLeft ? 1 : 0) + (hRight ? 1 : 0);
            if (wallCount == 2)
            {
                bool hasVertical = vAbove || vBelow;
                bool hasHorizontal = hLeft || hRight;
                
                // L-junction: one vertical + one horizontal wall
                if (hasVertical && hasHorizontal)
                {
                    float jx = gridOrigin.x + x * (cs + gap) - gap * 0.5f;
                    float jy = gridOrigin.y + y * (cs + gap) - gap * 0.5f;
                    
                    // L-junction outline (behind, slightly larger)
                    if (outlineThickness > 0f)
                    {
                        GameObject coverOutline = new GameObject("L_Cover_Outline");
                        coverOutline.transform.SetParent(wallParent);
                        coverOutline.transform.position = new Vector3(jx, jy, 0f);
                        coverOutline.transform.localScale = new Vector3(gap + outlineThickness * 2f, gap + outlineThickness * 2f, 1f);
                        
                        SpriteRenderer coSr = coverOutline.AddComponent<SpriteRenderer>();
                        coSr.sprite = GetPxSprite();
                        coSr.color = activeOutlineColor;
                        coSr.sortingLayerName = wallSortingLayerName;
                        coSr.sortingOrder = wallOutlineSortingOrder;
                    }
                    
                    // L-junction fill (foreground)
                    GameObject cover = new GameObject("L_Cover");
                    cover.transform.SetParent(wallParent);
                    cover.transform.position = new Vector3(jx, jy, 0f);
                    cover.transform.localScale = new Vector3(gap, gap, 1f);
                    
                    SpriteRenderer coverSr = cover.AddComponent<SpriteRenderer>();
                    coverSr.sprite = GetPxSprite();
                    coverSr.color = activeWallColor;
                    coverSr.sortingLayerName = wallSortingLayerName;
                    coverSr.sortingOrder = wallSortingOrder;
                }
            }
        }

    }

    // â”€â”€ Helpers â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    // MakeSeg: spawns wall with outline (2 sprites: outline background + fill foreground)
    void MakeSeg(string lbl, float wx, float wy, float sw, float sh, string layer, int order)
    {
        // Outline sprite (behind, slightly larger, darker)
        if (outlineThickness > 0f)
        {
            GameObject outline = new GameObject(lbl + "_Outline");
            outline.transform.SetParent(wallParent);
            outline.transform.position = new Vector3(wx, wy, 0f);
            outline.transform.localScale = new Vector3(sw + outlineThickness * 2f, sh + outlineThickness * 2f, 1f);
            
            SpriteRenderer outlineSr = outline.AddComponent<SpriteRenderer>();
            outlineSr.sprite = GetPxSprite();
            outlineSr.color = activeOutlineColor;
            outlineSr.sortingLayerName = layer;
            outlineSr.sortingOrder = wallOutlineSortingOrder;
        }
        
        // Main wall sprite (foreground)
        GameObject go = new GameObject(lbl);
        go.tag = "Wall";
        go.transform.SetParent(wallParent);
        go.transform.position = new Vector3(wx, wy, 0f);
        go.transform.localScale = new Vector3(sw, sh, 1f);

        SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = GetPxSprite();
        sr.color = activeWallColor;
        sr.sortingLayerName = layer;
        sr.sortingOrder = order;

        BoxCollider2D bc = go.AddComponent<BoxCollider2D>();
        bc.size = Vector2.one;
    }

    // Returns the world-space centre of grid cell (c.x, c.y)
    Vector3 CellCentre(Vector2Int c)
        => gridOrigin + new Vector3(c.x * (cs + gap) + cs * 0.5f,
                                    c.y * (cs + gap) + cs * 0.5f, 0f);

    void ApplyGlobalSortingIfEnabled()
    {
        if (!useGlobalSortingConfig)
        {
            return;
        }

        TankBattleGlobalConfig config = ResolveGlobalConfig();
        if (config == null || !config.ApplyGlobalSorting)
        {
            return;
        }

        floorSortingLayerName = config.FloorSortingLayerName;
        wallSortingLayerName = config.WallSortingLayerName;
        floorTilemapSortingOrder = config.FloorTilemapSortingOrder;
        floorBackgroundSortingOrder = config.FloorBackgroundSortingOrder;
        wallSortingOrder = config.WallSortingOrder;
        wallOutlineSortingOrder = config.WallOutlineSortingOrder;
    }

    TankBattleGlobalConfig ResolveGlobalConfig()
    {
        if (globalConfig == null)
        {
            globalConfig = TankBattleGlobalConfig.Instance;
        }

        return globalConfig;
    }

    void ApplyFloorTilemapSorting()
    {
        if (floorTilemap == null)
        {
            return;
        }

        TilemapRenderer tilemapRenderer = floorTilemap.GetComponent<TilemapRenderer>();
        if (tilemapRenderer == null)
        {
            return;
        }

        tilemapRenderer.sortingLayerName = floorSortingLayerName;
        tilemapRenderer.sortingOrder = floorTilemapSortingOrder;
    }

    Sprite GetPxSprite()
    {
        if (_pxSprite != null) return _pxSprite;
        
        // Simple 1x1 white pixel sprite
        Texture2D t = new Texture2D(1, 1);
        t.SetPixel(0, 0, Color.white);
        t.Apply();
        
        _pxSprite = Sprite.Create(t, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);
        return _pxSprite;
    }
}



