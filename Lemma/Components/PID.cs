﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Xna.Framework;

namespace Lemma.Components
{
	public class PID : Component, IUpdateableComponent
	{
		public Property<float> Input = new Property<float> { Editable = false };
		public Property<float> Target = new Property<float> { Editable = false };
		public Property<float> Output = new Property<float> { Editable = false };

		public Property<float> P = new Property<float> { Editable = true, Value = 1.0f };
		public Property<float> I = new Property<float> { Editable = true, Value = 1.0f };
		public Property<float> D = new Property<float> { Editable = true, Value = 1.0f };

		public Property<float> PreviousError = new Property<float> { Editable = false };
		public Property<float> Integral = new Property<float> { Editable = false };

		public override void InitializeProperties()
		{
			this.EnabledInEditMode.Value = false;
			this.EnabledWhenPaused.Value = false;
		}

		public void Update(float dt)
		{
			float error = this.Target - this.Input;
			this.Integral.Value += error * dt;
			float derivative = (error - this.PreviousError) / dt;
			this.Output.Value = (this.P * error) + (this.I * this.Integral) + (this.D * derivative);
			this.PreviousError.Value = error;
		}
	}

	public class PID3 : Component, IUpdateableComponent
	{
		public Property<Vector3> Input = new Property<Vector3> { Editable = false };
		public Property<Vector3> Target = new Property<Vector3> { Editable = false };
		public Property<Vector3> Output = new Property<Vector3> { Editable = false };

		public Property<float> P = new Property<float> { Editable = true };
		public Property<float> I = new Property<float> { Editable = true };
		public Property<float> D = new Property<float> { Editable = true };

		public Property<Vector3> PreviousError = new Property<Vector3> { Editable = false };
		public Property<Vector3> Integral = new Property<Vector3> { Editable = false };

		public override void InitializeProperties()
		{
			this.EnabledInEditMode.Value = false;
			this.EnabledWhenPaused.Value = false;
		}

		public void Update(float dt)
		{
			Vector3 error = this.Target.Value - this.Input;
			this.Integral.Value += error * dt;
			Vector3 derivative = (error - this.PreviousError) / dt;
			this.Output.Value = (this.P * error) + (this.I * this.Integral.Value) + (this.D * derivative);
			this.PreviousError.Value = error;
		}
	}
}
