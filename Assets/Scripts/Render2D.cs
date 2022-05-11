using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Render2D : MonoBehaviour
{
    [SerializeField] private ComputeShader computeShader;
    [SerializeField] private int n = 256;
    [SerializeField] private float velocityAmount = 100f;
    [SerializeField] private float densityAmount = 300f;
    [SerializeField] private float diffusionFactor = 0.0f;
    [SerializeField] private float timeStep = 0.125f;
    [SerializeField] private bool useDeltaTime = true;

    private float aDiffuse;
    private float cDiffuse;
    private int size;
    private RenderTexture renderTexture;
    private float[] empty;
    private Dictionary<string, ComputeBuffer> computeBuffers;
    private int threads;
    private int numthreads = 256;

    // Indexes of kernels in ComputeShader
    static class Kernels
    {
        public const int AddSource = 0;
        public const int Diffusion = 1;
        public const int Advection = 2;
        public const int ProjectionFirstPart = 3;
        public const int ProjectionLastPart = 4;
    }

    void Start()
    {
        size = n * n;
        computeShader.SetInt("n", n);
        threads = size / numthreads + 1;
        empty = new float[size];

        computeBuffers = new Dictionary<string, ComputeBuffer>();
        computeBuffers.Add("u", new ComputeBuffer(size, sizeof(float)));
        computeBuffers.Add("u0", new ComputeBuffer(size, sizeof(float)));
        computeBuffers.Add("v", new ComputeBuffer(size, sizeof(float)));
        computeBuffers.Add("v0", new ComputeBuffer(size, sizeof(float)));
        computeBuffers.Add("d", new ComputeBuffer(size, sizeof(float)));
        computeBuffers.Add("d0", new ComputeBuffer(size, sizeof(float)));

        InitializeTexture(ref renderTexture);
    }

    void Update()
    {   
        
        if (Input.GetMouseButtonDown(1))
        {
            computeBuffers["d"].SetData(empty);
            computeBuffers["u"].SetData(empty);
            computeBuffers["v"].SetData(empty);
        }

        if (useDeltaTime)
        {
            computeShader.SetFloat("dt", Time.deltaTime);
        }
        else
        {
            computeShader.SetFloat("dt", timeStep);
        }

        aDiffuse = Time.deltaTime * diffusionFactor * n * n;
        cDiffuse = 1 + 4 * aDiffuse;

        GetFromUI("d0", "v0");
        VelocityStep("u", "v", "u0", "v0", aDiffuse, cDiffuse);
        DensityStep("d", "d0", "u", "v", aDiffuse, cDiffuse);

    }
    
    void OnDestroy()
    {
        renderTexture.Release();
        foreach (KeyValuePair<string, ComputeBuffer> entry in computeBuffers)
        {
            if (entry.Value != null)
            {
                entry.Value.Release();
            }
        }
    }

    void InitializeTexture(ref RenderTexture rt)
    {
        if (rt == null)
        {
            rt = new RenderTexture(n, n, 0);
            rt.enableRandomWrite = true;
            rt.Create();
        }
    }

    void GetFromUI(string d, string v)
    {
        computeBuffers[d].SetData(empty);
        Vector3 mousePos = Camera.main.ScreenToViewportPoint(Input.mousePosition) * n;
        Vector3Int indexVec = Vector3Int.FloorToInt(mousePos);
        int index = indexVec.x + n * indexVec.y;

        float[] densityField = new float[size];
        float[] velocityField = new float[size];
        int sourceIndex = (int)(size / 10 + 10000);
        densityField[sourceIndex] = densityAmount;
        velocityField[sourceIndex] = velocityAmount;

        if (Input.GetMouseButton(0) && index > 0 && index < size)
        {
            densityField[index] = densityAmount;
            velocityField[index] = velocityAmount;
        }
        computeBuffers[d].SetData(densityField);
        computeBuffers[v].SetData(velocityField);
    }

    void VelocityStep(string u, string v, string u0, string v0, float a, float c)
    {
        AddSource(u, u0); AddSource(v, v0);
        Swap(u, u0); Diffuse(u, u0, a, c);
        Swap(v, v0); Diffuse(v, v0, a, c);
        Project(u, v, u0, v0);
        Swap(u0, u); Swap(v0, v);
        Advect(u, u0, u0, v0, false); Advect(v, v0, u0, v0, false);
        Project(u, v, u0, v0);
    }

    void DensityStep(string x, string x0, string u, string v, float a, float c)
    {
        AddSource(x, x0);
        Swap(x, x0); Diffuse(x, x0, a, c);
        Swap(x, x0); Advect(x, x0, u, v, true);
    }

    void AddSource(string x, string s)
    {
        computeShader.SetBuffer(Kernels.AddSource, "x", computeBuffers[x]);
        computeShader.SetBuffer(Kernels.AddSource, "s", computeBuffers[s]);
        computeShader.Dispatch(Kernels.AddSource, threads, 1, 1);
    }

    void Diffuse(string x, string x0, float a, float c)
    {
        computeShader.SetBuffer(Kernels.Diffusion, "x", computeBuffers[x]);
        computeShader.SetBuffer(Kernels.Diffusion, "x0", computeBuffers[x0]);
        computeShader.SetFloat("a", a);
        computeShader.SetFloat("c", c);
        for (int k = 0; k < 20; k++)
        {
            computeShader.Dispatch(Kernels.Diffusion, threads, 1, 1);
        }
    }

    void Advect(string d, string d0, string u, string v, bool draw)
    {
        computeShader.SetTexture(Kernels.Advection, "renderTexture", renderTexture);
        computeShader.SetBuffer(Kernels.Advection, "d", computeBuffers[d]);
        computeShader.SetBuffer(Kernels.Advection, "d0_copy", computeBuffers[d0]);
        computeShader.SetBuffer(Kernels.Advection, "u_copy", computeBuffers[u]);
        computeShader.SetBuffer(Kernels.Advection, "v_copy", computeBuffers[v]);
        computeShader.SetBool("draw", draw);
        computeShader.Dispatch(Kernels.Advection, threads, 1, 1);
    }

    void Project(string u, string v, string p, string div)
    {
        computeShader.SetBuffer(Kernels.ProjectionFirstPart, "u", computeBuffers[u]);
        computeShader.SetBuffer(Kernels.ProjectionFirstPart, "v", computeBuffers[v]);
        computeShader.SetBuffer(Kernels.ProjectionFirstPart, "p", computeBuffers[p]);
        computeShader.SetBuffer(Kernels.ProjectionFirstPart, "div", computeBuffers[div]);
        computeShader.Dispatch(Kernels.ProjectionFirstPart, threads, 1, 1);

        float a = 1.0f;
        float c = 4.0f;
        Diffuse(p, div, a, c);

        computeShader.SetBuffer(Kernels.ProjectionLastPart, "u", computeBuffers[u]);
        computeShader.SetBuffer(Kernels.ProjectionLastPart, "v", computeBuffers[v]);
        computeShader.SetBuffer(Kernels.ProjectionLastPart, "p", computeBuffers[p]);
        computeShader.Dispatch(Kernels.ProjectionLastPart, threads, 1, 1);
    }

    void Swap(string a, string b)
    {
        var tmp = computeBuffers[a];
        computeBuffers[a] = computeBuffers[b];
        computeBuffers[b] = tmp;
    }

    private void OnRenderImage(RenderTexture src, RenderTexture dest)
    {
        Graphics.Blit(renderTexture, dest);
    }
}

