using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Render2D : MonoBehaviour
{
    [SerializeField] private ComputeShader computeShader;
    [SerializeField] private RenderTexture inputTexture;
    [SerializeField] private RenderTexture outputTexture;

    private RenderTexture[] u;
    private RenderTexture[] v;

    private int size = 256;
    private float dt = 0.05f;
    private float range = 0.5f;
    private int iterations = 20;

    private void CreateTextures(ref RenderTexture[] rt)
    {
        rt[0] = new RenderTexture(size, size, 24);
        rt[0].enableRandomWrite = true;

        rt[0].Create();

        rt[1] = new RenderTexture(size, size, 24);
        rt[1].enableRandomWrite = true;

        rt[1].Create();
    }

    private void InitializeTextures()
    {
        //Initialize RenderTextures
        if (inputTexture == null)
        {
            inputTexture = new RenderTexture(size, size, 24);
            inputTexture.enableRandomWrite = true;

            inputTexture.Create();
        }
        if (outputTexture == null)
        {
            outputTexture = new RenderTexture(size, size, 24);
            outputTexture.enableRandomWrite = true;

            outputTexture.Create();
        }

        u = new RenderTexture[2];
        v = new RenderTexture[2];

        CreateTextures(ref u);
        CreateTextures(ref v);
    }

    private void Start()
    {
        InitializeTextures();

        //Initialize Params
        computeShader.SetTexture(0, "Result", outputTexture);
        computeShader.SetFloat("dt", dt);
        computeShader.SetFloat("Resolution", outputTexture.width);
        computeShader.SetFloat("Range", range);
        computeShader.SetFloat("Diff", 0.001f);
        computeShader.SetFloat("N", size);

        computeShader.SetTexture(2, "Result", outputTexture);
        computeShader.SetTexture(2, "u", u[0]);
        computeShader.SetTexture(2, "v", v[0]);
        computeShader.SetTexture(2, "uPrev", u[1]);
        computeShader.SetTexture(2, "vPrev", v[1]);

        computeShader.Dispatch(2, inputTexture.width / 8, inputTexture.height / 8, 1);
    }

    void Swap(ref RenderTexture[] textures)
    {
        RenderTexture temp = textures[0];
        textures[0] = textures[1];
        textures[1] = temp;
    }

    private void SwapInputOutput()
    {
        RenderTexture tempTex = inputTexture;
        inputTexture = outputTexture;
        outputTexture = tempTex;
    }

    private void AddSourceTexture(ref RenderTexture inTex, ref RenderTexture outTex)
    {
        computeShader.SetTexture(4, "Input", inTex);
        computeShader.SetTexture(4, "Result", outTex);
        computeShader.Dispatch(4, inputTexture.width / 8, inputTexture.height / 8, 1);
    }

    private void Diffuse(ref RenderTexture inTex, ref RenderTexture outTex)
    {
        computeShader.SetTexture(1, "Input", inTex);
        computeShader.SetTexture(1, "Result", outTex);
        computeShader.Dispatch(1, inputTexture.width / 8, inputTexture.height / 8, 1);
    }
    private void Advect(ref RenderTexture inTex, ref RenderTexture outTex)
    {
        computeShader.SetTexture(3, "Input", inTex);
        computeShader.SetTexture(3, "Result", outTex);
        computeShader.Dispatch(3, inputTexture.width / 8, inputTexture.height / 8, 1);
    }

    private void DensityStep()
    {
        //Add source
        if (Input.GetMouseButton(0))
        {

            Vector3 mousePos = Camera.main.ScreenToViewportPoint(Input.mousePosition);

            computeShader.SetFloat("mouseX", mousePos.x * size);
            computeShader.SetFloat("mouseY", mousePos.y * size);

            computeShader.Dispatch(0, inputTexture.width / 8, inputTexture.height / 8, 1);
        }

        //Diffuse
        for (int i = 0; i < iterations; i++)
        {
            Diffuse(ref outputTexture, ref inputTexture);

            SwapInputOutput();
        }

        //Advect
        SwapInputOutput();

        computeShader.SetTexture(3, "u", u[0]);
        computeShader.SetTexture(3, "v", v[0]);
        Advect(ref inputTexture, ref outputTexture);
    }

    private void VelocityStep()
    {
        AddSourceTexture(ref u[1], ref u[0]);
        AddSourceTexture(ref v[1], ref v[0]);

        Swap(ref u); Diffuse(ref u[1], ref u[0]);
        Swap(ref v); Diffuse(ref v[1], ref v[0]);

        //Project

        Swap(ref u); Swap(ref v);

        
    }

    private void DensityFade()
    {
        computeShader.SetTexture(5, "Result", outputTexture);
        computeShader.Dispatch(5, inputTexture.width / 8, inputTexture.height / 8, 1);
    }

    private void FixedUpdate()
    {
        VelocityStep();
        DensityStep();
        DensityFade();
    }
    
    private void OnRenderImage(RenderTexture src, RenderTexture dest)
    {
        Graphics.Blit(outputTexture, dest);
    }
}
