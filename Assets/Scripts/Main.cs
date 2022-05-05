using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Main : MonoBehaviour
{
    [SerializeField] private ComputeShader computeShader;
    [SerializeField] private RenderTexture renderTexture;

    [SerializeField] private float diffusionFactor;

    [SerializeField] private RenderTexture previousTexture;
    [SerializeField] private RenderTexture velocityField;

    private int size = 512;
    private int speed = 1;
    private int xThreads, yThreads;

    static class Kernels
    {
        public const int Initialize = 0;
        public const int Diffuse = 1;
        public const int Advection = 2;
        public const int UserInput = 3;
        public const int InitializeVelocity = 4;
    }


    void InitializeTextures()
    {
        if (renderTexture == null)
        {
            renderTexture = new RenderTexture(size, size, 24);
            renderTexture.enableRandomWrite = true;

            renderTexture.Create();
        }

        if (previousTexture == null)
        {
            previousTexture = new RenderTexture(size, size, 24);
            previousTexture.enableRandomWrite = true;

            previousTexture.Create();
        }

        if (velocityField == null)
        {
            velocityField = new RenderTexture(size, size, 24);
            velocityField.enableRandomWrite = true;

            velocityField.Create();
        }
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

    void Project()
    {

    }

    void FixedUpdate()
    {
        Graphics.CopyTexture(renderTexture, previousTexture);

        computeShader.SetFloat("dt", Time.deltaTime); // Set deltaTime

        
        Diffuse();
        //Project();
        Advect();
        //Project();
        UserInput();

    }

    private void OnRenderImage(RenderTexture src, RenderTexture dest)
    {
        Graphics.Blit(renderTexture, dest);
    }
}
