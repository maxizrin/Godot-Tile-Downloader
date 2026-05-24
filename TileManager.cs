using Godot;
using System;
using System.Collections.Generic;

public partial class TileManager : Node
{

    [Export] Control layout2;
    [Export] Control layout4;
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
    Vector2I currentTile = new Vector2I(-1, -1);

    [Export] ButtonGroup group;

    public override void _Ready()
    {
        base._Ready();

        group.Pressed += (btn) =>
        {
            // TODO: On button press, set target tile for actions.
        };
    }

    public void ZoomOut()
    {
        if (currentZoom < 1)
        {
            // We can zoom no more.
            return;
        }

        currentTile = tilePath.Pop();
        LoadTile(currentTile);
    }

    public void ZoomIn()
    {
        tilePath.Push(currentTile);
        // TODO: Get the tile coordinates.
        currentTile = new Vector2I();
        LoadTile(currentTile);
    }

    private void LoadTile(Vector2I targetTile)
    {
        // TODO: Set the correct layout, 2 or 4.
        // TODO: Set number labels.
        // TODO: Set lon/lan labels.
        // TODO: Load background image, if we have it.
        // TODO: Load images of children, if we have them.
    }

    public void DownloadTile()
    {
        // TODO: Download the tile.
        // TODO: Have tile URL format configureable.
    }

    public void DownloadTileRecursive()
    {

    }

    public void SetDownloadRecZoom(int zoom)
    {
        downloadRecZoom = zoom;
        // TODO: Remove.        
        GD.Print(downloadRecZoom);
    }
}
