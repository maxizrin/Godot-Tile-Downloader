using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

public partial class TileManager : Node
{
    private bool requestPending = false;
    [ExportGroup("Tile Manager")]
    [ExportSubgroup("Map UI")]
    [Export] Control layout2;
    [Export] Control layout4;
    [Export] ButtonGroup group;
    /// <summary>
    ///  The tile we are zoomed in on, if any data exists for it.
    /// Only relavent for zoom levels higher than 0.
    /// </summary>
    [Export] TextureRect tileBackground;

    [ExportSubgroup("Connection")]
    [Export] MenuButton urlPresets;
    [Export] TextEdit tileFormat;

    [ExportSubgroup("Labels")]
    [Export] Label batchDownloadCount;
    [Export] Label zoomLevel;
    [Export] Label sizeApprox;

    private int downloadRecDepth = 1;
    int currentZoom => tilePath.Count;
    /// <summary>
    /// The current, and cooridnates of tiles, as we traverse the map.
    /// The last element is the current tile we're looking at.
    /// If the array is empty, we're at zoom level 0.    
    /// </summary>
    Stack<Vector2I> tilePath = [];
    /// <summary>
    /// Coordinates of the tile we are currently viewing.
    /// </summary>
    /// <returns></returns>
    Vector2I currentTile = new Vector2I(-1, -1);
    /// <summary>
    /// Currently selected child tile.
    /// </summary>
    Tile targetTile = null;


    private bool is2TileLayout => (layout2.GetChild(1, false) as Control).Visible;

    public override void _Ready()
    {
        base._Ready();

        LoadTile(currentTile);

        List<Tuple<string, string>> presets = [
            // Google maps, road map.
            new Tuple<string,string> ("Google - street",
            "https://mt1.google.com/vt/x={x}&y={y}&z={z}"),
            // Google maps, sat map.
            new Tuple<string,string> ("Google - satellite",
            "https://mt1.google.com/vt/lyrs=y&x={x}&y={y}&z={z}"),
            // Open street map.
            new Tuple<string,string> ("Open street - with markings",
            "https://a.tile.openstreetmap.fr/hot/{z}/{x}/{y}.png"),
            new Tuple<string,string>("Open street - topography",
            "https://opentopomap.org/{z}/{x}/{y}.png"),
            new Tuple<string,string>("CartoDB Positron",
            "https://a.basemaps.cartocdn.com/light_all/{z}/{x}/{y}.png"),
            new Tuple<string,string>("CartoDB Positron - no labels",
            "https://a.basemaps.cartocdn.com/rastertiles/light_nolabels/{z}/{x}/{y}.png"),
            new Tuple<string,string>("CartoDB Positron - dark",
            "https://a.basemaps.cartocdn.com/rastertiles/dark_all/{z}/{x}/{y}.png"),
            new Tuple<string,string>("CartoDB Positron - dark, no labels",
            "https://a.basemaps.cartocdn.com/rastertiles/dark_nolabels/{z}/{x}/{y}.png"),
            new Tuple<string,string>("National Satellite",
            "https://basemap.nationalmap.gov/arcgis/rest/services/USGSImageryOnly/MapServer/tile/{z}/{y}/{x}"),
        ];

        var popup = urlPresets.GetPopup();
        // Populate the presets menu.
        presets.ForEach(p => { popup.AddItem(p.Item1); });
        // On menu item pressed.
        popup.IdPressed += (id) => tileFormat.Text = presets[(int)id].Item2;

        group.Pressed += (btn) =>
        {
            // On button press, set target tile for actions.
            if (btn is not Tile t)
                return;

            targetTile = t;
        };

        var btns = group.GetButtons();
        for (int i = 0; i < btns.Count; i++)
        {
            var tile = btns[i] as Tile;
            tile.onDoubleClicked += () =>
            {
                tile.ButtonPressed = true;
                targetTile = tile;
                ZoomIn();
            };
            tile.onDownloadClicked += () =>
            {
                tile.ButtonPressed = true;
                targetTile = tile;
                StartDownload(tile.coords, currentZoom, true);
            };
        }

        UpdateSizeLabel();
    }

    private void UpdateSizeLabel()
    {
        CheckSize().ContinueWith((s) =>
       {
           var sizes = new string[]
           {
                " bytes",
                " KBytes",
                " MBytes",
           };

           var sizeInMemory = s.Result;
           for (int i = 0; i < sizes.Length; i++)
           {
               if (sizeInMemory < 1024)
               {
                   sizeApprox.CallDeferred("set", "text", sizeInMemory.ToString() + sizes[i]);
                   return;
               }
               sizeInMemory /= 1024;
           }
           sizeApprox.CallDeferred("set", "text", sizeInMemory.ToString() + " GBytes");
       });
    }


    public async Task<long> CheckSize()
    {
        if (!Directory.Exists("./Tiles"))
            return 0;
        DirectoryInfo dirInfo = new DirectoryInfo("./Tiles");
        long dirSize = await Task.Run(() => dirInfo.EnumerateFiles("*", SearchOption.AllDirectories).Sum(file => file.Length));
        return dirSize;
    }

    private string GetFilePath(Vector2I coords, int zoom, string type, bool createDir)
    {
        var dir = $"./Tiles/{zoom}/{coords.X}";
        if (createDir)
        {
            Directory.CreateDirectory(dir);
        }
        return dir + $"/{coords.Y}." + type;
    }

    public void ResetZoom()
    {
        while (currentZoom > 0)
        {
            currentTile = tilePath.Pop();
        }
        targetTile = null;
        OnZoomChanged();
        LoadTile(currentTile);
    }

    public void ZoomOut()
    {
        if (currentZoom <= 0)
        {
            // We can zoom no more.
            return;
        }
        currentTile = tilePath.Pop();
        zoomLevel.Text = $"{currentZoom}";
        OnZoomChanged();
        LoadTile(currentTile);
    }

    public void ZoomIn()
    {
        if (targetTile == null)
        {
            return;
        }
        tilePath.Push(currentTile);
        zoomLevel.Text = $"{currentZoom}";
        currentTile = targetTile.coords;
        OnZoomChanged();
        LoadTile(currentTile);
    }

    private void OnZoomChanged()
    {
        targetTile = null;
        var btns = group.GetButtons();
        for (int i = 0; i < btns.Count; i++)
        {
            btns[i].ButtonPressed = false;
        }

        SetDownloadRecZoom(downloadRecDepth);
    }

    private string FormatGPSAngle(float decimalGPS, bool isLon)
    {
        string letter;
        if (isLon)
        {
            letter = decimalGPS < 0 ? "W" : "E";
        }
        else
        {
            letter = decimalGPS < 0 ? "S" : "N";
        }

        decimalGPS = Math.Abs(decimalGPS);
        var degs = (int)decimalGPS;
        var minutes = (decimalGPS - degs) * 60;
        var seconds = (minutes - (int)minutes) * 60;
        var secondsFraction = (int)((seconds - (int)seconds) * 1000);

        if (isLon)
        {
            return $"{degs:000}°{(int)minutes:00}'{(int)seconds:00}.{secondsFraction:000}\"" + letter;
        }
        else
        {
            return $"{degs:00}°{(int)minutes:00}'{(int)seconds:00}.{secondsFraction:000}\"" + letter;
        }
    }


    private void LoadTile(Vector2I targetTile)
    {
        // Set the correct layout, 2 or 4.
        layout2.Visible = currentZoom <= 0;
        layout4.Visible = currentZoom > 0;
        var activeLayout = layout2.Visible ? layout2 : layout4;
        // Set number labels.
        var coords = GetChildrenTilesCoords(targetTile, currentZoom);
        var children = activeLayout.GetChildren();
        for (int i = 0; i < coords.Length; i++)
        {
            var tile = children[i].GetChild<Tile>();

            // Set lon/lan labels.
            tile.SetData(coords[i]);
            // Load images of children, if we have them.

            if (TryLoadTexture(coords[i], currentZoom, out var t))
            {
                tile.SetTexture(t);
            }
            else
            {
                tile.SetTexture(null);
            }
        }

        // Load background image, if we have it.

        if (TryLoadTexture(currentTile, currentZoom - 1, out var bt))
        {
            tileBackground.Texture = bt;
        }
        else
        {
            tileBackground.Texture = null;
        }

    }

    private bool TryLoadTexture(Vector2I coords, int zoom, out ImageTexture t)
    {
        string[] fileTypes = ["jpg", "png"];
        for (int j = 0; j < fileTypes.Length; j++)
        {
            if (HasTile(coords, zoom, out var filePath))
            {
                var cImg = Image.LoadFromFile(filePath);
                t = ImageTexture.CreateFromImage(cImg);
                return true;
            }
        }
        t = null;
        return false;
    }


    public bool HasTile(Vector2I coords, int zoom, out string path)
    {
        path = null;
        string[] fileTypes = ["jpg", "png"];
        for (int j = 0; j < fileTypes.Length; j++)
        {
            var filePath = GetFilePath(coords, zoom, fileTypes[j], false);
            if (File.Exists(filePath))
            {
                path = filePath;
                return true;
            }
        }
        return false;
    }


    /// <summary>
    /// Returns the top-left, and bottom-right, bounds of the tile in GPS coordinates.
    /// </summary>
    /// <param name="tileCoords">X and Y of the tile</param>
    /// <param name="zoom">Zoom level</param>
    /// <returns></returns>
    private Vector2[] GetLatLon(Vector2I tileCoords, int zoom)
    {
        if (zoom <= 0)
        {
            return [
                new Vector2(-180,-90),
                    new Vector2(180,90)
            ];
        }

        // // If we have 2 tiles on zoom level 0, then the longitude is divided by half.
        var vertTiles = (float)Math.Pow(2, zoom);
        var horzTiles = (float)(Math.Pow(2, zoom) * (is2TileLayout ? 2 : 1));

        var tileSize = new Vector2(360f / horzTiles, -180f / vertTiles);
        // var bl = new Vector2(tileCoords.X * tileSize.X - 180, 90 + tileCoords.Y * tileSize.Y);
        // var bl = new Vector2(tileCoords.X * tileSize.X - 180, (float)lat_deg);
        // return [bl, bl + tileSize];

        var n = 2 ^ zoom;
        var lonMin = tileCoords.X * 360.0 / horzTiles - 180.0;
        var lonMax = (tileCoords.X + 1) * 360.0 / horzTiles - 180.0;
        var latMin = Math.Atan(Math.Sinh(Math.PI * (1 - 2.0 * tileCoords.Y / vertTiles)));
        var latMax = Math.Atan(Math.Sinh(Math.PI * (1 - 2.0 * (tileCoords.Y + 1) / vertTiles)));
        var latMaxDeg = latMin * 180.0 / Math.PI;
        var latMinDeg = latMax * 180.0 / Math.PI;

        if (tileCoords.Y == 0)
        {
            latMin = -90;
        }
        if (tileCoords.Y == vertTiles - 1)
        {
            latMax = 90;
        }


        return [
            new Vector2((float)lonMin,(float)latMinDeg),
            new Vector2((float)lonMax,(float)latMaxDeg)
            ];
    }


    public void DownloadTile()
    {
        StartDownload(targetTile.coords, currentZoom, true);
    }

    public void DownloadTileRecursive()
    {
        if (targetTile == null)
        {
            return;
        }

        var coords = new List<List<Vector2I>>
        {
            new List<Vector2I>(GetChildrenTilesCoords(targetTile.coords, currentZoom))
        };

        // MAX_TODO: Fix this, wrong coordinates are being generated.
        
        GD.Print(currentZoom + 1, ", ", currentZoom + downloadRecDepth + 1);
        for (int zoom = currentZoom + 1, i = 0; zoom < currentZoom + downloadRecDepth + 1; zoom++, i++)
        {
            var res = new List<Vector2I>();
            coords[i].ForEach(prevLayerTile =>
            {
                res.AddRange(GetChildrenTilesCoords(prevLayerTile, zoom));
            });

            coords.Add(res);
        }

        // Only assign the first.
        var assign = true;
        for (int i = 0; i < coords.Count; i++)
        {
            var zoom = currentZoom + i;
            for (int j = 0; j < coords[i].Count; j++)
            {
                StartDownload(coords[i][j], zoom, assign);
                assign = false;
            }
        }
    }

    private void StartDownload(Vector2I coords, int zoom, bool assign)
    {
        GD.Print(coords, ", ", zoom);
        if (HasTile(coords, zoom, out var _))
        {
            return;
        }


        var url = tileFormat.Text
            .Replace("{x}", coords.X.ToString())
            .Replace("{y}", coords.Y.ToString())
            .Replace("{z}", zoom.ToString());

        var request = new HttpRequest();
        AddChild(request);
        request.RequestCompleted += (result, responseCode, headers, body) =>
          {
              if (targetTile == null)
              {
                  return;
              }

              var image = new Image();

              var contentType = headers.FirstOrDefault(h => h.Contains("Content-Type:"))?.Split(":")[1].Trim().ToLower();

              switch (contentType)
              {
                  case "image/png":
                      image.LoadPngFromBuffer(body);
                      // Save image in folder.
                      image.SavePng(GetFilePath(coords, zoom, "png", true));
                      break;
                  case "image/jpeg":
                      image.LoadJpgFromBuffer(body);
                      // Save image in folder.
                      image.SaveJpg(GetFilePath(coords, zoom, "jpg", true));
                      break;
                  default:
                      return;
              }
              UpdateSizeLabel();
              var texture = ImageTexture.CreateFromImage(image);

              // Assign to texture.
              if (assign)
                  targetTile.SetTexture(texture);
              request.QueueFree();
          };

        if (request.IsNodeReady())
        {
            request.Request(url);
        }
        else
        {
            request.Ready += () => request.Request(url);
        }
    }

    public void SetDownloadRecZoom(int zoom)
    {
        downloadRecDepth = zoom;

        // geometric series formula for sum of powers of X^i where i = 0 to n.
        // (X^(n+1) - 1) / (X - 1)
        // For X = 4, the formula is: (4^(n+1) - 1) / 3.
        // This is only true for zoom level 0, otherwise, we start from the second term.
        // For all other zoom levels it's the same equation with n + 1, minus the first term which is always 1.

        if (currentZoom == 0)
        {
            var count = (int)((Math.Pow(4, zoom + 1) - 1) / 3);
            batchDownloadCount.Text = count.ToString();
        }
        else
        {
            var count = (int)((Math.Pow(4, zoom + 2) - 1) / 3) - 1; // subtract the first term which is always 1;
            batchDownloadCount.Text = count.ToString();
        }
    }

    private Vector2I[] GetChildrenTilesCoords(Vector2I parentCoord, int zoomLevel)
    {
        if (zoomLevel <= 0)
        {
            if (is2TileLayout)
            {
                return [new Vector2I(0, 0), new Vector2I(1, 0)];
            }
            return [new Vector2I(0, 0)];
        }
        var x = parentCoord.X * 2;
        var y = parentCoord.Y * 2;
        return [
            new Vector2I(x, y),
            new Vector2I(x + 1, y),
            new Vector2I(x, y + 1),
            new Vector2I(x + 1, y + 1)
        ];
    }
}
