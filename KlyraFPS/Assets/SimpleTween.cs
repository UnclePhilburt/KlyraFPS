using UnityEngine;
using UnityEngine.UI;
using System;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// Simple tweening utility - drop-in replacement for basic LeanTween functionality.
/// No external dependencies required.
/// </summary>
public static class LeanTween
{
    private static MonoBehaviour runner;
    private static Dictionary<int, Coroutine> activeTweens = new Dictionary<int, Coroutine>();
    private static int tweenId = 0;

    static MonoBehaviour GetRunner()
    {
        if (runner == null)
        {
            GameObject obj = new GameObject("_TweenRunner");
            UnityEngine.Object.DontDestroyOnLoad(obj);
            runner = obj.AddComponent<TweenRunner>();
        }
        return runner;
    }

    public static TweenDescr scale(GameObject target, Vector3 to, float time)
    {
        var descr = new TweenDescr();
        var coroutine = GetRunner().StartCoroutine(ScaleRoutine(target.transform, to, time, descr));
        return descr;
    }

    public static TweenDescr alphaCanvas(CanvasGroup target, float to, float time)
    {
        var descr = new TweenDescr();
        var coroutine = GetRunner().StartCoroutine(AlphaCanvasRoutine(target, to, time, descr));
        return descr;
    }

    public static TweenDescr moveLocal(GameObject target, Vector3 to, float time)
    {
        var descr = new TweenDescr();
        var coroutine = GetRunner().StartCoroutine(MoveLocalRoutine(target.transform, to, time, descr));
        return descr;
    }

    public static TweenDescr alpha(GameObject target, float to, float time)
    {
        var descr = new TweenDescr();
        var graphic = target.GetComponent<Graphic>();
        if (graphic != null)
        {
            var coroutine = GetRunner().StartCoroutine(AlphaGraphicRoutine(graphic, to, time, descr));
        }
        return descr;
    }

    public static TweenDescr value(GameObject target, Color from, Color to, float time)
    {
        var descr = new TweenDescr();
        GetRunner().StartCoroutine(ColorValueRoutine(from, to, time, descr));
        return descr;
    }

    public static TweenDescr value(GameObject target, float from, float to, float time)
    {
        var descr = new TweenDescr();
        GetRunner().StartCoroutine(FloatValueRoutine(from, to, time, descr));
        return descr;
    }

    static IEnumerator ScaleRoutine(Transform target, Vector3 to, float time, TweenDescr descr)
    {
        Vector3 from = target.localScale;
        float elapsed = 0f;

        while (elapsed < time)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = descr.ApplyEase(Mathf.Clamp01(elapsed / time));
            target.localScale = Vector3.LerpUnclamped(from, to, t);
            yield return null;
        }

        target.localScale = to;
    }

    static IEnumerator AlphaCanvasRoutine(CanvasGroup target, float to, float time, TweenDescr descr)
    {
        float from = target.alpha;
        float elapsed = 0f;

        while (elapsed < time)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = descr.ApplyEase(Mathf.Clamp01(elapsed / time));
            target.alpha = Mathf.LerpUnclamped(from, to, t);
            yield return null;
        }

        target.alpha = to;
    }

    static IEnumerator MoveLocalRoutine(Transform target, Vector3 to, float time, TweenDescr descr)
    {
        Vector3 from = target.localPosition;
        float elapsed = 0f;

        while (elapsed < time)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = descr.ApplyEase(Mathf.Clamp01(elapsed / time));
            target.localPosition = Vector3.LerpUnclamped(from, to, t);
            yield return null;
        }

        target.localPosition = to;
    }

    static IEnumerator AlphaGraphicRoutine(Graphic target, float to, float time, TweenDescr descr)
    {
        float from = target.color.a;
        float elapsed = 0f;

        while (elapsed < time)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = descr.ApplyEase(Mathf.Clamp01(elapsed / time));
            Color c = target.color;
            c.a = Mathf.LerpUnclamped(from, to, t);
            target.color = c;
            yield return null;
        }

        Color final = target.color;
        final.a = to;
        target.color = final;
    }

    static IEnumerator ColorValueRoutine(Color from, Color to, float time, TweenDescr descr)
    {
        float elapsed = 0f;

        while (elapsed < time)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = descr.ApplyEase(Mathf.Clamp01(elapsed / time));
            Color current = Color.LerpUnclamped(from, to, t);
            descr.InvokeColorUpdate(current);
            yield return null;
        }

        descr.InvokeColorUpdate(to);
    }

    static IEnumerator FloatValueRoutine(float from, float to, float time, TweenDescr descr)
    {
        float elapsed = 0f;

        while (elapsed < time)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = descr.ApplyEase(Mathf.Clamp01(elapsed / time));
            float current = Mathf.LerpUnclamped(from, to, t);
            descr.InvokeFloatUpdate(current);
            yield return null;
        }

        descr.InvokeFloatUpdate(to);
    }
}

public class TweenDescr
{
    public enum EaseType { Linear, OutQuad, InQuad, OutBack, InOutQuad }
    private EaseType easeType = EaseType.Linear;
    private Action<Color> onColorUpdate;
    private Action<float> onFloatUpdate;

    public TweenDescr setEaseOutQuad()
    {
        easeType = EaseType.OutQuad;
        return this;
    }

    public TweenDescr setEaseInQuad()
    {
        easeType = EaseType.InQuad;
        return this;
    }

    public TweenDescr setEaseOutBack()
    {
        easeType = EaseType.OutBack;
        return this;
    }

    public TweenDescr setEaseInOutQuad()
    {
        easeType = EaseType.InOutQuad;
        return this;
    }

    public TweenDescr setOnUpdate(Action<Color> callback)
    {
        onColorUpdate = callback;
        return this;
    }

    public TweenDescr setOnUpdate(Action<float> callback)
    {
        onFloatUpdate = callback;
        return this;
    }

    public void InvokeColorUpdate(Color c)
    {
        onColorUpdate?.Invoke(c);
    }

    public void InvokeFloatUpdate(float f)
    {
        onFloatUpdate?.Invoke(f);
    }

    public float ApplyEase(float t)
    {
        switch (easeType)
        {
            case EaseType.OutQuad:
                return 1f - (1f - t) * (1f - t);
            case EaseType.InQuad:
                return t * t;
            case EaseType.OutBack:
                float c1 = 1.70158f;
                float c3 = c1 + 1f;
                return 1f + c3 * Mathf.Pow(t - 1f, 3f) + c1 * Mathf.Pow(t - 1f, 2f);
            case EaseType.InOutQuad:
                return t < 0.5f ? 2f * t * t : 1f - Mathf.Pow(-2f * t + 2f, 2f) / 2f;
            default:
                return t;
        }
    }
}

public class TweenRunner : MonoBehaviour { }
