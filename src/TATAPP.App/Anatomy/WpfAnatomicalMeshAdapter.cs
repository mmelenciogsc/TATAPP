using System.Windows;
using System.Windows.Media.Media3D;
using TATAPP.Core;

namespace TATAPP.App.Anatomy;

/// <summary>Materializes the authoritative platform-neutral anatomy mesh for WPF.</summary>
internal static class WpfAnatomicalMeshAdapter
{
    public static MeshGeometry3D Create(AnatomicalTriangleMesh source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var mesh = new MeshGeometry3D();
        foreach (var vertex in source.Vertices)
        {
            mesh.Positions.Add(new Point3D(vertex.Position.X, vertex.Position.Y, vertex.Position.Z));
            mesh.Normals.Add(new Vector3D(vertex.Normal.X, vertex.Normal.Y, vertex.Normal.Z));
            mesh.TextureCoordinates.Add(new Point(vertex.SurfacePoint.U, vertex.SurfacePoint.V));
        }
        foreach (var index in source.TriangleIndices) mesh.TriangleIndices.Add(index);
        mesh.Freeze();
        return mesh;
    }
}
