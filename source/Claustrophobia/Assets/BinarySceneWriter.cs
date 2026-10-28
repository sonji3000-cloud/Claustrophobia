using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

using System.IO;
using UnityEditor;
using System.Text;

public class BinarySceneWriter : MonoBehaviour
{
    private List<string> m_strGameObjectNames = new List<string>();
    private List<string> m_strTextureNames = new List<string>();

    int k = 0;

    void WriteMatrix(BinaryWriter binaryWriter, Matrix4x4 matrix)
    {
        binaryWriter.Write(matrix.m00);
        binaryWriter.Write(matrix.m10);
        binaryWriter.Write(matrix.m20);
        binaryWriter.Write(matrix.m30);
        binaryWriter.Write(matrix.m01);
        binaryWriter.Write(matrix.m11);
        binaryWriter.Write(matrix.m21);
        binaryWriter.Write(matrix.m31);
        binaryWriter.Write(matrix.m02);
        binaryWriter.Write(matrix.m12);
        binaryWriter.Write(matrix.m22);
        binaryWriter.Write(matrix.m32);
        binaryWriter.Write(matrix.m03);
        binaryWriter.Write(matrix.m13);
        binaryWriter.Write(matrix.m23);
        binaryWriter.Write(matrix.m33);
    }

    void WriteLocalMatrix(BinaryWriter binaryWriter, Transform transform)
    {
        Matrix4x4 matrix = Matrix4x4.identity;
        matrix.SetTRS(transform.localPosition, transform.localRotation, transform.localScale);
        WriteMatrix(binaryWriter, matrix);
    }

    void WriteWorldMatrix(BinaryWriter binaryWriter, Transform transform)
    {
        Matrix4x4 matrix = Matrix4x4.identity;
        matrix.SetTRS(transform.position, transform.rotation, transform.lossyScale);
        WriteMatrix(binaryWriter, matrix);
    }

    void WriteColor(BinaryWriter binaryWriter, Color color)
    {
        binaryWriter.Write(color.r);
        binaryWriter.Write(color.g);
        binaryWriter.Write(color.b);
        binaryWriter.Write(color.a);
    }

    void Write2DVector(BinaryWriter binaryWriter, Vector2 v)
    {
        binaryWriter.Write(v.x);
        binaryWriter.Write(v.y);
    }

    void Write2DVectors(BinaryWriter binaryWriter, string strName, Vector2[] pVectors)
    {
        binaryWriter.Write(strName);
        binaryWriter.Write(pVectors.Length);
        foreach (Vector2 v in pVectors) Write2DVector(binaryWriter, v);
    }

    void Write3DVector(BinaryWriter binaryWriter, Vector3 v)
    {
        binaryWriter.Write(v.x);
        binaryWriter.Write(v.y);
        binaryWriter.Write(v.z);
    }

    void Write3DVectors(BinaryWriter binaryWriter, string strName, Vector3[] pVectors)
    {
        binaryWriter.Write(strName);
        binaryWriter.Write(pVectors.Length);
        foreach (Vector3 v in pVectors) Write3DVector(binaryWriter, v);
    }

    void Write4DVector(BinaryWriter binaryWriter, Vector4 v)
    {
        binaryWriter.Write(v.x);
        binaryWriter.Write(v.y);
        binaryWriter.Write(v.z);
        binaryWriter.Write(v.w);
    }

    void Write4DVectors(BinaryWriter binaryWriter, string strName, Vector4[] pVectors)
    {
        binaryWriter.Write(strName);
        binaryWriter.Write(pVectors.Length);
        foreach (Vector4 v in pVectors) Write4DVector(binaryWriter, v);
    }

    void WriteIntegers(BinaryWriter binaryWriter, int[] pIntegers)
    {
        binaryWriter.Write(pIntegers.Length);
        foreach (int i in pIntegers) binaryWriter.Write(i);
    }

    void WriteIntegers(BinaryWriter binaryWriter, string strName, int[] pIntegers)
    {
        binaryWriter.Write(strName);
        binaryWriter.Write(pIntegers.Length);
        foreach (int i in pIntegers) binaryWriter.Write(i);
    }

    void WriteBoundingBox(BinaryWriter binaryWriter, string strName, Bounds bounds)
    {
        binaryWriter.Write(strName);
        Write3DVector(binaryWriter, bounds.center);
        Write3DVector(binaryWriter, bounds.extents);
    }

    void WriteMesh(BinaryWriter binaryWriter,
               string strName,
               Mesh mesh,
               string strObjectName,
               Transform transform)
    {
        binaryWriter.Write(strName);
        binaryWriter.Write(mesh.name);

        BinaryWriter meshBinaryWriter =
            new BinaryWriter(File.Open(strObjectName + ".bin",
                                       FileMode.Create));

        //---------------------------------
        // Scale 반영 Bounds 생성
        //---------------------------------

        Bounds bounds = mesh.bounds;

        Vector3 scaledExtents =
            Vector3.Scale(bounds.extents,
                          transform.lossyScale);

        Bounds scaledBounds =
            new Bounds(Vector3.zero,
                       scaledExtents * 2.0f);

        WriteBoundingBox(meshBinaryWriter,
                         "<BoundingBox>:",
                         scaledBounds);

        //---------------------------------

        Write3DVectors(meshBinaryWriter,
                       "<Positions>:",
                       mesh.vertices);

        Write3DVectors(meshBinaryWriter,
                       "<Normals>:",
                       mesh.normals);

        Write2DVectors(meshBinaryWriter,
                       "<TextureCoords>:",
                       mesh.uv);

        WriteIntegers(meshBinaryWriter,
                      "<Indices>:",
                      mesh.triangles);

        meshBinaryWriter.Flush();
        meshBinaryWriter.Close();
    }

    bool FindObjectByName(string strObjectName)
    {
        for (int i = 0; i < m_strGameObjectNames.Count; i++)
        {
            if (m_strGameObjectNames.Contains(strObjectName)) return (true);
        }
        m_strGameObjectNames.Add(strObjectName);
        return (false);
    }

    void WriteFrameInfo(BinaryWriter sceneBinaryWriter, Transform transform)
    {
        MeshFilter meshFilter = transform.gameObject.GetComponent<MeshFilter>();
        MeshRenderer meshRenderer = transform.gameObject.GetComponent<MeshRenderer>();

        if (meshFilter && meshRenderer)
        {
            string strToRemove = " (";
            int i = transform.name.IndexOf(strToRemove);
            string strObjectName = (i > 0) ? string.Copy(transform.name).Remove(i) : string.Copy(transform.name);

            string strFilteredObjectName = strObjectName.Replace(' ', '_');
            sceneBinaryWriter.Write("<GameObject>:");
            sceneBinaryWriter.Write(strFilteredObjectName);

            WriteWorldMatrix(sceneBinaryWriter, transform);

            if (FindObjectByName(strFilteredObjectName) == false)
            {
                WriteMesh(sceneBinaryWriter, "<Mesh>:", meshFilter.mesh, strFilteredObjectName, transform);
            }
        }
    }

    void WriteFrameHierarchy(BinaryWriter sceneBinaryWriter, Transform transform)
    {
        WriteFrameInfo(sceneBinaryWriter, transform);

        for (int k = 0; k < transform.childCount; k++)
        {
            WriteFrameHierarchy(sceneBinaryWriter, transform.GetChild(k));
        }
    }

    int GetChildObjectsCount(Transform transform)
    {
        int nChilds = 0;
        MeshFilter meshFilter = transform.gameObject.GetComponent<MeshFilter>();
        MeshRenderer meshRenderer = transform.gameObject.GetComponent<MeshRenderer>();
        if (meshFilter && meshRenderer) nChilds++;

        for (int k = 0; k < transform.childCount; k++) nChilds += GetChildObjectsCount(transform.GetChild(k));

        return (nChilds);
    }

    int GetAllGameObjectsCount()
    {
        int nGameObjects = 0;

        Scene scene = transform.gameObject.scene;
        GameObject[] rootGameObjects = scene.GetRootGameObjects();
        foreach (GameObject gameObject in rootGameObjects) nGameObjects += GetChildObjectsCount(gameObject.transform);

        return (nGameObjects);
    }

    void Start()
    {
        BinaryWriter sceneBinaryWriter = new BinaryWriter(File.Open("Scene.bin", FileMode.Create));

        int nGameObjects = GetAllGameObjectsCount();
        sceneBinaryWriter.Write("<GameObjects>:");
        sceneBinaryWriter.Write(nGameObjects);

        Scene scene = transform.gameObject.scene;
        GameObject[] rootGameObjects = scene.GetRootGameObjects();
        foreach (GameObject gameObject in rootGameObjects) WriteFrameHierarchy(sceneBinaryWriter, gameObject.transform);

        sceneBinaryWriter.Flush();
        sceneBinaryWriter.Close();

        print("Scene Write Completed");
    }
}
