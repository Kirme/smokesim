using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class Render2D : MonoBehaviour
{
    [SerializeField] private ComputeShader computeShader;
    [SerializeField] private int n = 256;
    [SerializeField] private float velocityAmount = 100f;
    [SerializeField] private float densityAmount = 300f;
    [SerializeField] private float diffusionFactor = 0.0f;
    [SerializeField] private float timeStep = 0.0f;
    [SerializeField] private bool useDeltaTime = true;
    [SerializeField] private InputField densityInput;
    [SerializeField] private InputField velocityInput;

    private float aDiffuse;
    private float cDiffuse;
    private int size;
    private RenderTexture renderTexture;
    private float[] empty;
    private Dictionary<string, ComputeBuffer> computeBuffers;
    private int threads;
    private int numthreads = 256;

    // Color
    private float rScale = 1.0f, gScale, bScale;

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
        // Set values of variables
        size = n * n;
        computeShader.SetInt("n", n);
        threads = size / numthreads + 1;
        empty = new float[size];

        // Add common variables to computeShader
        computeBuffers = new Dictionary<string, ComputeBuffer>();
        computeBuffers.Add("u", new ComputeBuffer(size, sizeof(float)));
        computeBuffers.Add("u0", new ComputeBuffer(size, sizeof(float)));
        computeBuffers.Add("v", new ComputeBuffer(size, sizeof(float)));
        computeBuffers.Add("v0", new ComputeBuffer(size, sizeof(float)));
        computeBuffers.Add("d", new ComputeBuffer(size, sizeof(float)));
        computeBuffers.Add("d0", new ComputeBuffer(size, sizeof(float)));

        // Initialize renderTexture
        InitializeTexture(ref renderTexture);
    }

    void Update()
    {   
        // Clear screen on RMB
        if (Input.GetMouseButtonDown(1))
        {
            computeBuffers["d"].SetData(empty);
            computeBuffers["u"].SetData(empty);
            computeBuffers["v"].SetData(empty);
        }

        // Handle option to use custom timestep
        if (useDeltaTime)
        {
            computeShader.SetFloat("dt", Time.deltaTime);
        }
        else
        {
            computeShader.SetFloat("dt", timeStep);
        }

        // Set variables related to diffusion calculation
        aDiffuse = Time.deltaTime * diffusionFactor * n * n;
        cDiffuse = 1 + 4 * aDiffuse;

        // Main algorithm steps
        GetFromUI("d0", "v0"); // Get current values
        VelocityStep("u", "v", "u0", "v0", aDiffuse, cDiffuse); // Calculate velocity
        DensityStep("d", "d0", "u", "v", aDiffuse, cDiffuse); // Calculate density

    }
    
    // Release some values when object is destroyed
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

    // Initialize a renderTexture
    void InitializeTexture(ref RenderTexture rt)
    {
        if (rt == null)
        {
            rt = new RenderTexture(n, n, 0); // Set size
            rt.enableRandomWrite = true; // Enable write to texture
            rt.Create(); // Create texture
        }
    }

    // Get values from previous iteration
    void GetFromUI(string d, string v)
    {
        computeBuffers[d].SetData(empty); // Clear previous data
        Vector3 mousePos = Camera.main.ScreenToViewportPoint(Input.mousePosition) * n; // Get mouse position on screen
        Vector3Int indexVec = Vector3Int.FloorToInt(mousePos); // Translate mouse position to integers
        int index = indexVec.x + n * indexVec.y; // Translate mouse index to array index

        float[] densityField = new float[size]; // Create new density field
        float[] velocityField = new float[size]; // Create new velocity field
        int sourceIndex = (int)(size / 10 + 10000); // Define location of constant smoke source
        densityField[sourceIndex] = densityAmount; // Set density of constant source
        velocityField[sourceIndex] = velocityAmount; // Set velocity of constant source

        if (Input.GetMouseButton(0) && index > 0 && index < size) // If mouse is on screen and LMB pressed
        {
            densityField[index] = densityAmount; // Set density of user created source
            velocityField[index] = velocityAmount; // Set velocity of user created source
        }
        computeBuffers[d].SetData(densityField); // Send density field to computeShader
        computeBuffers[v].SetData(velocityField); // Send velocity field to computeShader
    }

    // Calculate velocity
    void VelocityStep(string u, string v, string u0, string v0, float a, float c)
    {
        AddSource(u, u0); AddSource(v, v0); // Add the forces u0 and v0 to the velocities u and v
        Swap(u, u0); Diffuse(u, u0, a, c); // Calculate diffusion for u
        Swap(v, v0); Diffuse(v, v0, a, c); // Calculate diffusion for v
        Project(u, v, u0, v0);
        Swap(u0, u); Swap(v0, v);
        Advect(u, u0, u0, v0, false); Advect(v, v0, u0, v0, false); // Calculate self-advection (velocity field is moved along itself)
        Project(u, v, u0, v0);
    }

    // Calculate density through adding sources, calculating diffusion, and calculating advection
    void DensityStep(string x, string x0, string u, string v, float a, float c)
    {
        AddSource(x, x0);
        Swap(x, x0); Diffuse(x, x0, a, c);
        Swap(x, x0); Advect(x, x0, u, v, true);
    }

    // Add a fluid source s to x
    void AddSource(string x, string s)
    {
        computeShader.SetBuffer(Kernels.AddSource, "x", computeBuffers[x]);
        computeShader.SetBuffer(Kernels.AddSource, "s", computeBuffers[s]);
        computeShader.Dispatch(Kernels.AddSource, threads, 1, 1);
    }

    // Setup computeShader for calculating diffusion
    void Diffuse(string x, string x0, float a, float c)
    {
        computeShader.SetBuffer(Kernels.Diffusion, "x", computeBuffers[x]);
        computeShader.SetBuffer(Kernels.Diffusion, "x0", computeBuffers[x0]);
        computeShader.SetFloat("a", a);
        computeShader.SetFloat("c", c);
        for (int k = 0; k < 20; k++) // Some number of iterations, not exact and found through testing
        {
            computeShader.Dispatch(Kernels.Diffusion, threads, 1, 1);
        }
    }

    // Setup computeShader for calculating advection
    void Advect(string d, string d0, string u, string v, bool draw)
    {
        computeShader.SetTexture(Kernels.Advection, "renderTexture", renderTexture);
        computeShader.SetBuffer(Kernels.Advection, "d", computeBuffers[d]);
        computeShader.SetBuffer(Kernels.Advection, "d0_copy", computeBuffers[d0]);
        computeShader.SetBuffer(Kernels.Advection, "u_copy", computeBuffers[u]);
        computeShader.SetBuffer(Kernels.Advection, "v_copy", computeBuffers[v]);
        computeShader.SetBool("draw", draw);

        computeShader.SetFloat("rScale", rScale);
        computeShader.SetFloat("gScale", gScale);
        computeShader.SetFloat("bScale", bScale);

        computeShader.Dispatch(Kernels.Advection, threads, 1, 1);
    }

    // Setup computeShader for calculating projection
    void Project(string u, string v, string p, string div)
    {
        // Setup first part of projection
        computeShader.SetBuffer(Kernels.ProjectionFirstPart, "u", computeBuffers[u]);
        computeShader.SetBuffer(Kernels.ProjectionFirstPart, "v", computeBuffers[v]);
        computeShader.SetBuffer(Kernels.ProjectionFirstPart, "p", computeBuffers[p]);
        computeShader.SetBuffer(Kernels.ProjectionFirstPart, "div", computeBuffers[div]);
        computeShader.Dispatch(Kernels.ProjectionFirstPart, threads, 1, 1);

        // Use diffusion kernel to calculate pressure
        float a = 1.0f;
        float c = 4.0f;
        Diffuse(p, div, a, c);

        // Setup second part of projection
        computeShader.SetBuffer(Kernels.ProjectionLastPart, "u", computeBuffers[u]);
        computeShader.SetBuffer(Kernels.ProjectionLastPart, "v", computeBuffers[v]);
        computeShader.SetBuffer(Kernels.ProjectionLastPart, "p", computeBuffers[p]);
        computeShader.Dispatch(Kernels.ProjectionLastPart, threads, 1, 1);
    }

    // Swap two computeBuffers by changing references
    void Swap(string a, string b)
    {
        var tmp = computeBuffers[a];
        computeBuffers[a] = computeBuffers[b];
        computeBuffers[b] = tmp;
    }

    // Draw renderTexture on screen
    private void OnRenderImage(RenderTexture src, RenderTexture dest)
    {
        Graphics.Blit(renderTexture, dest);
    }

    // UI Functions
    /*
        The following functions handle inputs from the options menu
        They are relatively simple, and mostly allow for the input of a single value
    */

    public void SwitchDeltaTime()
    {
        useDeltaTime = !useDeltaTime;
    }

    public void SetTimeStep(float newTS)
    {
        timeStep = newTS;
    }

    // Check that input is a non-negative float
    private bool CheckValidFloat(string input)
    {
        float floatInput;

        bool isFloat = float.TryParse(input, out floatInput);

        if (!isFloat)
            return false;

        return floatInput >= 0.0f;
    }

    public void SetVelocity()
    {
        string velocity = velocityInput.text;

        if (CheckValidFloat(velocity))
        {
            float input;
            float.TryParse(velocity, out input);

            velocityAmount = input;
        }
    }

    public void SetDensity()
    {
        string density = densityInput.text;

        if (CheckValidFloat(density))
        {
            float input;
            float.TryParse(density, out input);

            densityAmount = input;
        }
    }

    public void SetDiffusion(float diff)
    {
        diffusionFactor = diff;
    }

    public void SetRedScale(float scale)
    {
        rScale = scale;
    }

    public void SetGreenScale(float scale)
    {
        gScale = scale;
    }

    public void SetBlueScale(float scale)
    {
        bScale = scale;
    }
}

