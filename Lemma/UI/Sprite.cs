﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework;
using System.IO;

namespace Lemma.Components
{
	public class Sprite : UIComponent
	{
		public Property<string> Image = new Property<string> { Editable = true };
		public Property<bool> IsStandardImage = new Property<bool> { Value = false, Editable = true };
		public Property<Color> Tint = new Property<Color> { Value = new Color(255, 255, 255, 255), Editable = true };
		public Property<float> Opacity = new Property<float> { Value = 1.0f, Editable = true };
		public Property<SpriteEffects> Effects = new Property<SpriteEffects> { Value = SpriteEffects.None, Editable = true };
		private Texture2D texture;

		public override void InitializeProperties()
		{
			base.InitializeProperties();
			this.Image.Set = delegate(string value)
			{
				this.Image.InternalValue = value;
				this.loadTexture(value);
			};
		}

		public override void LoadContent(bool reload)
		{
			base.LoadContent(reload);
			if (reload && this.Image.Value != null)
				this.loadTexture(this.Image);
		}

		private void loadTexture(string file)
		{
			if (file != null)
			{
				if (this.IsStandardImage)
				{
					using (Stream stream = File.OpenRead(file))
						this.texture = Texture2D.FromStream(this.main.GraphicsDevice, stream);
				}
				else
					this.texture = this.main.Content.Load<Texture2D>(file);
				this.Size.Value = new Vector2(this.texture.Width, this.texture.Height);
			}
		}

		protected override void draw(GameTime time, Matrix parent, Matrix transform)
		{
			if (this.texture != null)
			{
				Vector2 position = Vector2.Transform(this.Position, parent);
				float rotation = this.Rotation + (float)Math.Atan2(parent.M12, parent.M11);
				Vector2 scale = this.Scale;
				scale.X *= (float)Math.Sqrt((parent.M11 * parent.M11) + (parent.M12 * parent.M12));
				scale.Y *= (float)Math.Sqrt((parent.M21 * parent.M21) + (parent.M22 * parent.M22));
				this.renderer.Batch.Draw(this.texture, position, null, this.Tint.Value * this.Opacity.Value, rotation, this.AnchorPoint.Value * this.Size, scale, this.Effects, 0);
			}
		}
	}
}
