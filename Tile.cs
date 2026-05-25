using Godot;
using System;

public partial class Tile : Button
{
    [Export] Node root;
    [Export] Label xCoord;
    [Export] Label yCoord;
    [Export] Label lat;
    [Export] Label lon;
    [Export] TextureRect texture;
    public Vector2I coords { get; private set; }
    public int index => root.GetIndex(false);
    public void SetData(Vector2I coordinates, Vector2 lonLat)
    {
        coords = coordinates;
        xCoord.Text = $"{coordinates.X}";
        yCoord.Text = $"{coordinates.Y}";
        lon.Text = $"{lonLat.X}";
        lat.Text = $"{lonLat.Y}";
    }

    public void SetTexture(Texture2D tileTexture)
    {
        texture.Texture = tileTexture;
    }
}
