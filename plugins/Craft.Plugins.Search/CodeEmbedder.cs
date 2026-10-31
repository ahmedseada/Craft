// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Text.Json.Nodes;
using Craft.Abstractions;
using Idrak.Abstraction;
using Idrak.Abstraction.Generation;
using Idrak.Models;
using Idrak.Text.Retrieval;

namespace Craft.Plugins.Search;

/// <summary>
/// The chunks' and the queries' vectors: an encoder model (a family registered in Idrak's <c>PretrainedEncoders</c> by
/// the setting idrakPlugins: BERT's in Idrak.Text.Plugins) through Idrak.Text's <see cref="TextEncoder"/>, its tokens
/// pooled by the family's pooler (<c>SequencePoolers</c>: "first" for BERT's), with the tokenizer's special tokens, on the
/// device of the setting. One call at a time (the encoder's layers are shared).
/// </summary>
internal sealed class CodeEmbedder : IDisposable
{
    private readonly Lock _gate = new();
    private readonly PretrainedEncoderModel _model;
    private readonly TextEncoder _encoder;

    private CodeEmbedder(string name, PretrainedEncoderModel model, TextEncoder encoder)
    {
        Name = name;
        _model = model;
        _encoder = encoder;
    }

    /// <summary>The model's name as the setting gives it.</summary>
    public string Name { get; }

    /// <summary>The vectors' length (once one was made), else 0.</summary>
    public int Dimensions { get; private set; }

    /// <summary>The device it runs on.</summary>
    public string Device => _model.Device.ToString();

    /// <summary>The pooler (a name in SequencePoolers).</summary>
    public string Pooler => _encoder.Pooler;

    /// <summary>Loads the model of <paramref name="settings"/>; the libraries and families of the plug-in's folder registered first.</summary>
    /// <exception cref="NotSupportedException">The model's family is not registered (Idrak's message, naming PretrainedEncoders).</exception>
    public static CodeEmbedder Load(SearchSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        PluginIdrakLibraries.IncludePackages(typeof(CodeEmbedder).Assembly, settings.IdrakPlugins);
        string folder = ModelSource.Resolve(settings.EmbeddingModel);
        var device = settings.EmbeddingDevice.Equals("auto", StringComparison.OrdinalIgnoreCase) ? Idrak.Abstraction.Device.Default : Idrak.Abstraction.Device.Parse(settings.EmbeddingDevice);
        var model = PretrainedEncoderModel.Load(folder, new PretrainedEncoderOptions { Device = device });
        try
        {
            ITokenizer tokenizer = model.Tokenizer ?? throw new InvalidDataException($"The embedding model {settings.EmbeddingModel} has no tokenizer (tokenizer.json).");
            int length = Math.Min(settings.EmbeddingMaxTokens, model.Encoder.MaxPositions);
            int pad = model.Config["pad_token_id"] is JsonValue value && value.TryGetValue(out int id) ? id : 0;
            var encoder = new TextEncoder(model.Encoder, tokenizer, length, pad) { Pooler = model.Pooler, SpecialTokens = tokenizer is IPretrainedTokenizer };
            return new CodeEmbedder(settings.EmbeddingModel, model, encoder);
        }
        catch
        {
            model.Dispose();
            throw;
        }
    }

    /// <summary>The vectors of <paramref name="texts"/>, in order.</summary>
    public float[][] Embed(IReadOnlyList<string> texts, int batch)
    {
        lock (_gate)
        {
            var vectors = _encoder.Encode(texts, batch);
            if (vectors.Length > 0)
            {
                Dimensions = vectors[0].Length;
            }

            return vectors;
        }
    }

    public void Dispose() => _model.Dispose();
}
