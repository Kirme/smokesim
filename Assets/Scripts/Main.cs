using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Main : MonoBehaviour
{
    // Global Variables
    [SerializeField] private ComputeShader computeShader;
    [SerializeField] private RenderTexture renderTexture;
    [SerializeField] private RenderTexture previousTexture;

    [SerializeField] private RenderTexture velocityField;
    private RenderTexture velocityDivergence;
    private RenderTexture cachedVelocityField;

    [SerializeField] private RenderTexture pressureField;
    private RenderTexture previousPressure;
    private RenderTexture pressureGradient;

    [SerializeField] private RenderTexture temperatureField;

    [SerializeField] private float diffusionFactor;
    private float T0 = 0.5f;
    private float buoyancyScale = 10f;
    
    private int size = 512;
    private int speed = 1;
    private int xThreads, yThreads;
    private int projectIter = 20;

    // Indexes of kernels in computeShader
    static class Kernels
    {
        public const int Initialize = 0;
        public const int Diffuse = 1;
        public const int Advection = 2;
        public const int UserInput = 3;
        public const int InitializeVelocity = 4;
        public const int Project = 5;
        public const int Divergence = 6;
        public const int Gradient = 7;
        public const int SubtractGradient = 8;
        public const int ConstantSource = 9;
        public const int Buoyancy = 10;
        public const int InitializeTemperature = 11;
    }

    void InitializeTexture(ref RenderTexture rt)
    {
        if (rt == null)
        {
            rt = new RenderTexture(size, size, 24);
            rt.enableRandomWrite = true;

            rt.Create();
        }
    }

    void InitializeTextures()
    {
        InitializeTexture(ref renderTexture);
        InitializeTexture(ref previousTexture);

        InitializeTexture(ref velocityField);
        InitializeTexture(ref velocityDivergence);
        InitializeTexture(ref cachedVelocityField);

        InitializeTexture(ref pressureField);
        InitializeTexture(ref previousPressure);
        InitializeTexture(ref pressureGradient);

        InitializeTexture(ref temperatureField);
    }

    void Buoyancy()
    {
        computeShader.SetFloat("T0", T0);
        computeShader.SetFloat("buoyancyScale", buoyancyScale);
        computeShader.SetTexture(Kernels.Buoyancy, "Temperature", temperatureField);
        computeShader.SetTexture(Kernels.Buoyancy, "Velocity", velocityField);
        computeShader.Dispatch(Kernels.Buoyancy, xThreads, yThreads, 1);
    }

    void Start()
    {
        InitializeTextures();
        
        xThreads = renderTexture.width / 8;
        yThreads = renderTexture.height / 8;

        // Common Shader Variables
        computeShader.SetInt("speed", speed);

        // Initialize Texture
        computeShader.SetTexture(Kernels.Initialize, "Result", renderTexture);
        computeShader.Dispatch(Kernels.Initialize, xThreads, yThreads, 1);

        computeShader.SetTexture(Kernels.InitializeVelocity, "Velocity", velocityField);
        computeShader.Dispatch(Kernels.InitializeVelocity, xThreads, yThreads, 1);

        computeShader.SetFloat("screenHeight", Screen.height);
        computeShader.SetTexture(Kernels.InitializeTemperature, "Temperature", temperatureField);
        computeShader.Dispatch(Kernels.InitializeTemperature, xThreads, yThreads, 1);
    }

    void Diffuse()
    {
        computeShader.SetFloat("diffusionFactor", diffusionFactor);

        computeShader.SetTexture(Kernels.Diffuse, "Previous", previousTexture);
        computeShader.SetTexture(Kernels.Diffuse, "Result", renderTexture);
        computeShader.Dispatch(Kernels.Diffuse, xThreads, yThreads, 1);
    }

    void UserInput()
    {
        if (Input.GetMouseButton(0))
        {
            Vector3 mousePos = Camera.main.ScreenToViewportPoint(Input.mousePosition);
            computeShader.SetInt("mouseX", (int) (mousePos.x * size));
            computeShader.SetInt("mouseY", (int) (mousePos.y * size));
            computeShader.SetFloat("range", 2.0f);

            computeShader.SetTexture(Kernels.UserInput, "Result", renderTexture);
            computeShader.Dispatch(Kernels.UserInput, xThreads, yThreads, 1);
        }
    }

    void Advect()
    {
        Graphics.CopyTexture(renderTexture, previousTexture);

        computeShader.SetTexture(Kernels.Advection, "Velocity", velocityField);
        computeShader.SetTexture(Kernels.Advection, "Previous", previousTexture);
        computeShader.SetTexture(Kernels.Advection, "Result", renderTexture);
        computeShader.Dispatch(Kernels.Advection, xThreads, yThreads, 1);
    }

    void Divergence()
    {
        computeShader.SetTexture(Kernels.Divergence, "Velocity", velocityField);
        computeShader.SetTexture(Kernels.Divergence, "VelocityDivergence", velocityDivergence);
        computeShader.Dispatch(Kernels.Divergence, xThreads, yThreads, 1);
    }

    void Gradient()
    {
        computeShader.SetTexture(Kernels.Gradient, "VelocityDivergence", velocityDivergence);
        computeShader.SetTexture(Kernels.Gradient, "Previous", pressureField);
        computeShader.SetTexture(Kernels.Gradient, "Result", pressureGradient);
        computeShader.Dispatch(Kernels.Gradient, xThreads, yThreads, 1);
    }

    void SubtractGradient()
    {
        computeShader.SetTexture(Kernels.SubtractGradient, "Input", pressureGradient);
        computeShader.SetTexture(Kernels.SubtractGradient, "Result", velocityField);
        computeShader.Dispatch(Kernels.SubtractGradient, xThreads, yThreads, 1);
    }

    void Project()
    {
        Divergence();

        computeShader.SetTexture(Kernels.Project, "VelocityDivergence", velocityDivergence);

        for (int i = 0; i < projectIter; i++)
        {
            Graphics.CopyTexture(pressureField, previousPressure);

            computeShader.SetTexture(Kernels.Project, "Previous", previousPressure);
            computeShader.SetTexture(Kernels.Project, "Result", pressureField);
            computeShader.Dispatch(Kernels.Project, xThreads, yThreads, 1);
        }

        Gradient();

        SubtractGradient();
    }

    void FixedUpdate()
    {
        Graphics.CopyTexture(renderTexture, previousTexture);

        computeShader.SetFloat("dt", Time.deltaTime); // Set deltaTime

        
        Diffuse();
        Project();
        Advect();
        Buoyancy();
        Project();
        UserInput();

        computeShader.SetTexture(Kernels.ConstantSource, "Result", renderTexture);
        computeShader.Dispatch(Kernels.ConstantSource, xThreads, yThreads, 1);
    }

    private void OnRenderImage(RenderTexture src, RenderTexture dest)
    {
        Graphics.Blit(renderTexture, dest);
    }
}
