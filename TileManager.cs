using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

public partial class TileManager : Node
{
    [Export] HttpRequest httpRequest;
    private bool requestPending = false;
    [Export] TextEdit tileFormat;
    [Export] Control layout2;
    [Export] Control layout4;
    [Export] Label zoomLevel;
    /// <summary>
    ///  The tile we are zoomed in on, if any data exists for it.
    /// Only relavent for zoom levels higher than 0.
    /// </summary>
    [Export] TextureRect tileBackground;
    private int downloadRecZoom = 1;
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

    [Export] ButtonGroup group;

    public override void _Ready()
    {
        base._Ready();

        LoadTile(currentTile);

        group.Pressed += (btn) =>
        {
            // On button press, set target tile for actions.
            if (btn is not Tile t)
                return;

            targetTile = t;
        };

        httpRequest.RequestCompleted += (result, responseCode, headers, body) =>
        {
            requestPending = false;
            if (targetTile == null)
            {
                return;
            }

            var image = new Image();
            Error error;

            var contentType = headers.FirstOrDefault(h => h.Contains("Content-Type:"))?.Split(":")[1].Trim().ToLower();
            GD.Print("Request complete, headers: ", string.Join(", ", headers));
            GD.Print("Request complete, content type: ", contentType);
            switch (contentType)
            {
                case "image/png":
                    error = image.LoadPngFromBuffer(body);
                    // Save image in folder.
                    image.SavePng(GetFilePath(targetTile.coords, currentZoom, "png", true));
                    break;
                case "image/jpeg":
                    error = image.LoadJpgFromBuffer(body);
                    // Save image in folder.
                    image.SaveJpg(GetFilePath(targetTile.coords, currentZoom, "jpg", true));
                    break;
                default:
                    return;
            }
            GD.Print(error);
            var texture = ImageTexture.CreateFromImage(image);
            // Assign to texture.
            targetTile.SetTexture(texture);


        };
    }

    private string GetFilePath(Vector2I coords, int zoom, string type, bool createDir = false)
    {
        var dir = $"./Tiles/{zoom}/{coords.X}";
        if (!Directory.Exists(dir) && createDir)
            Directory.CreateDirectory(dir);
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
    }

    private void LoadTile(Vector2I targetTile)
    {
        // Set the correct layout, 2 or 4.
        layout2.Visible = currentZoom <= 0;
        layout4.Visible = currentZoom > 0;
        var activeLayout = layout2.Visible ? layout2 : layout4;
        // Set number labels.
        var coords = GetTileCoords(targetTile, currentZoom);
        var children = activeLayout.GetChildren();
        for (int i = 0; i < coords.Length; i++)
        {
            var tile = children[i].GetChild<Tile>();

            // Set lon/lan labels.
            tile.SetData(coords[i], GetLatLon(coords[i], currentZoom));
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
        GD.Print("Loading tile: ", coords, ", ", zoom);

        string[] fileTypes = ["jpg", "png"];
        for (int j = 0; j < fileTypes.Length; j++)
        {
            var filePath = GetFilePath(coords, zoom, fileTypes[j]);
            if (File.Exists(filePath))
            {
                var cImg = Image.LoadFromFile(filePath);
                t = ImageTexture.CreateFromImage(cImg);
                GD.Print("File: ", filePath, ", loaded.");
                return true;
            }
            else
            {
                GD.Print("File: ", filePath, ", not found.");
            }
        }
        t = null;
        return false;
    }


    public bool HasTile(Vector2I coords, int zoom)
    {
        string[] fileTypes = ["jpg", "png"];
        for (int j = 0; j < fileTypes.Length; j++)
        {
            var filePath = GetFilePath(coords, zoom, fileTypes[j]);
            if (File.Exists(filePath))
            {
                return true;
            }
        }
        return false;
    }


    private Vector2 GetLatLon(Vector2I vector2I, int currentZoom)
    {
        // MAX_TODO: Calculate lon/lat for coordinate vector.
        return Vector2.Zero;
    }


    public void DownloadTile()
    {
        if (requestPending)
        {
            return;
        }

        if (HasTile(targetTile.coords, currentZoom))
        {
            return;
        }

        requestPending = true;
        var url = tileFormat.Text
        .Replace("{x}", targetTile.coords.X.ToString())
        .Replace("{y}", targetTile.coords.Y.ToString())
        .Replace("{z}", currentZoom.ToString());
        // Download the tile.
        httpRequest.Request(url);
        GD.Print(url);
    }

    public void DownloadTileRecursive()
    {
        if (targetTile == null)
        {
            return;
        }

        // MAX_TODO: Implement.
        // DownloadRecursive(targetTile.coords);
    }

    public void SetDownloadRecZoom(int zoom)
    {
        downloadRecZoom = zoom;
    }

    private Vector2I[] GetTileCoords(Vector2I parentCoord, int zoomLevel)
    {
        if (zoomLevel <= 0)
        {
            return [new Vector2I(0, 0), new Vector2I(1, 0)];
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
