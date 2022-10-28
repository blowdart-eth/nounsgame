﻿using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace NGE.Engine;

public abstract class DrawContext
{
    public readonly SpriteBatch sb;
    private readonly RasterizerState rasterizer;
    private readonly Effect effect;

    protected DrawContext(SpriteBatch spriteBatch)
    {
        sb = spriteBatch;

        effect = new BasicEffect(spriteBatch.GraphicsDevice);
        rasterizer = new RasterizerState
        {
            CullMode = CullMode.CullCounterClockwiseFace
        };
    }

    public void Begin()
    {
        sb.Begin(SpriteSortMode.Deferred, null, SamplerState.PointClamp, null, rasterizer, effect);
    }

    public void BeginNoTransform()
    {
        sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, DepthStencilState.None, RasterizerState.CullNone);
    }

    public void End()
    {
        sb.End();
    }
}