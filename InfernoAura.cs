using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace InfernoAura
{
	// Visual only. Vanilla draws the Inferno Potion as a huge rotating flame ring in the
	// foreground (Main.DrawInfernoRings), over everything. This mod skips that draw and
	// paints a translucent fire aura instead, in the spirit of a "burning cape" aura: a
	// soft heat glow behind the body and a ring of flames on the ground around the feet
	// (far half behind the character, near half in front), breathing slowly, few embers. The damage lives in
	// Player.Update (the `inferno` flag) and is never touched.
	public class InfernoAura : Mod
	{
		public override void Load()
		{
			if (Main.dedServ) return;
			On_Main.DrawInfernoRings += SkipRing;
			On_Main.DrawPlayers_AfterProjectiles += DrawAurasBehindPlayers;
		}

		public override void Unload() => AuraTextures.Dispose();

		// Not calling orig = the vanilla ring is never drawn, for any player.
		private static void SkipRing(On_Main.orig_DrawInfernoRings orig, Main self) { }

		// The sprite batch is closed right before players are drawn and right after, so
		// the aura gets its own additive batches: the far half of the fire ring and the
		// heat glow go behind the players, the near half of the ring in front of their feet.
		private static void DrawAurasBehindPlayers(On_Main.orig_DrawPlayers_AfterProjectiles orig, Main self)
		{
			Safe(() => AuraRenderer.DrawAll(front: false));
			orig(self);
			Safe(() => AuraRenderer.DrawAll(front: true));
		}

		private static void Safe(Action draw)
		{
			try { draw(); }
			catch (Exception e)
			{
				try { Main.spriteBatch.End(); } catch { }
				ModContent.GetInstance<InfernoAura>().Logger.Warn("aura draw failed: " + e.Message);
			}
		}
	}

	internal static class AuraRenderer
	{
		private static readonly Color Ember = new Color(255, 90, 22);
		private static readonly Color Core = new Color(255, 180, 70);

		private const int Flames = 46;          // flames around the ground ring
		private const float RingRadius = 54f;   // half width of the ring, in pixels
		private const float Tilt = 0.24f;       // ring seen at an angle: height / width

		public static bool Shows(Player p) => p.active && !p.outOfRange && !p.dead && p.inferno;

		public static void DrawAll(bool front)
		{
			SpriteBatch sb = Main.spriteBatch;
			bool begun = false;
			for (int i = 0; i < Main.maxPlayers; i++)
			{
				Player p = Main.player[i];
				if (!Shows(p)) continue;
				if (!begun)
				{
					sb.Begin(SpriteSortMode.Deferred, BlendState.Additive, Main.DefaultSamplerState,
						DepthStencilState.None, Main.Rasterizer, null, Main.Transform);
					begun = true;
				}
				Draw(sb, p, front);
			}
			if (begun) sb.End();
		}

		private static void Draw(SpriteBatch sb, Player p, bool front)
		{
			float t = Main.GlobalTimeWrappedHourly;
			float phase = p.whoAmI * 1.7f;
			// Slow breathing: alive, never strobing.
			float a = (0.82f + 0.18f * MathF.Sin(t * 2.2f + phase)) * (p.invis ? 0.35f : 1f);

			Texture2D glow = AuraTextures.Glow;
			Vector2 gOrigin = glow.Size() / 2f;
			Vector2 center = p.Center - Main.screenPosition + new Vector2(0f, p.gfxOffY);
			Vector2 feet = new Vector2(center.X, p.Bottom.Y - Main.screenPosition.Y + p.gfxOffY - 8f);

			if (!front)
			{
				// Heat haze around the body, then glow pooled on the floor inside the ring.
				sb.Draw(glow, center, null, Ember * (0.34f * a), 0f, gOrigin, 136f / glow.Width, SpriteEffects.None, 0f);
				sb.Draw(glow, center + new Vector2(0f, 8f), null, Core * (0.14f * a), 0f, gOrigin, new Vector2(56f, 76f) / glow.Width, SpriteEffects.None, 0f);
				sb.Draw(glow, feet, null, Ember * (0.60f * a), 0f, gOrigin, new Vector2(RingRadius * 2.3f, RingRadius * 2.3f * Tilt) / glow.Width, SpriteEffects.None, 0f);
			}

			// Flames on the rim of an ellipse on the ground, crawling around it. Each one is
			// a soft vertical tongue whose height wobbles, anchored at its base.
			Vector2 baseOrigin = new Vector2(glow.Width / 2f, glow.Height * 0.62f);
			for (int k = 0; k < Flames; k++)
			{
				float ang = MathHelper.TwoPi * k / Flames + t * 0.55f;
				float depth = MathF.Sin(ang);            // > 0 = near side (in front of the feet)
				if ((depth > 0f) != front) continue;
				Vector2 pos = feet + new Vector2(MathF.Cos(ang) * RingRadius, depth * RingRadius * Tilt);
				float lick = 0.5f + 0.5f * MathF.Sin(ang * 3f + t * 4.3f + k) * MathF.Sin(ang * 5f - t * 3.1f + phase);
				float h = 16f + 20f * lick;
				float near = 0.75f + 0.25f * depth;     // far flames a bit dimmer
				sb.Draw(glow, pos, null, Ember * (0.80f * a * near), 0f, baseOrigin, new Vector2(17f, h) / glow.Width, SpriteEffects.None, 0f);
				sb.Draw(glow, pos + new Vector2(0f, 1f), null, Core * (0.60f * a * near), 0f, baseOrigin, new Vector2(8f, h * 0.55f) / glow.Width, SpriteEffects.None, 0f);
			}
		}
	}

	// Generated once, on the main thread, the first time it is drawn: no image assets
	// to ship, and the shape stays soft at any zoom.
	internal static class AuraTextures
	{
		private static Texture2D glow;

		public static Texture2D Glow => glow ??= MakeGlow(128);

		private static Texture2D MakeGlow(int size)
		{
			var data = new Color[size * size];
			for (int j = 0; j < size; j++)
			for (int i = 0; i < size; i++)
			{
				float x = (i + 0.5f) / size * 2f - 1f, y = (j + 0.5f) / size * 2f - 1f;
				float v = MathHelper.Clamp(1f - MathF.Sqrt(x * x + y * y), 0f, 1f);
				v = v * v * v;
				data[j * size + i] = new Color(v, v, v, v);
			}
			var tex = new Texture2D(Main.graphics.GraphicsDevice, size, size);
			tex.SetData(data);
			return tex;
		}

		public static void Dispose()
		{
			Texture2D g = glow;
			glow = null;
			if (g != null) Main.QueueMainThreadAction(() => g.Dispose());
		}
	}

	public class AuraPlayer : ModPlayer
	{
		// A few embers drifting up from the ring and a warm light. Deliberately sparse:
		// the aura is the glow, not particles all over the body.
		public override void PostUpdate()
		{
			if (Main.dedServ || !AuraRenderer.Shows(Player) || Main.gamePaused) return;

			if (Main.rand.NextBool(5))
			{
				float side = Main.rand.NextFloat(-1f, 1f);
				Vector2 pos = new Vector2(Player.Center.X + side * 54f, Player.Bottom.Y - 6f + Main.rand.NextFloat(-6f, 6f));
				Dust d = Dust.NewDustPerfect(pos, DustID.Torch, new Vector2(side * -0.2f, Main.rand.NextFloat(-1.6f, -0.7f)), 120, default, Main.rand.NextFloat(0.7f, 1.05f));
				d.noGravity = true;
				d.noLight = true;
			}

			float breathe = 0.8f + 0.2f * (float)Math.Sin(Main.GlobalTimeWrappedHourly * 2.2f + Player.whoAmI * 1.7f);
			Lighting.AddLight(Player.Center, 0.75f * breathe, 0.36f * breathe, 0.08f * breathe);
			Lighting.AddLight(Player.Bottom, 0.5f * breathe, 0.22f * breathe, 0.05f * breathe);
		}
	}
}
