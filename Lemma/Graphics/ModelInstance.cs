﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml.Serialization;
using Microsoft.Xna.Framework;
using System.Collections;
using System.Reflection;

namespace Lemma.Components
{
	public class ModelInstance : Component
	{
		public class ModelInstanceSystem : Model
		{
			public string Key;
			protected ListProperty<ModelInstance> instances = new ListProperty<ModelInstance>();
			protected IListBinding<ModelInstance> instanceBinding;
			protected Dictionary<string, IPropertyBinding> parameters = new Dictionary<string, IPropertyBinding>();
			protected List<IPropertyBinding> modifiedParameters = new List<IPropertyBinding>();

			public override void InitializeProperties()
			{
				base.InitializeProperties();
				this.CullBoundingBox.Value = false;
				this.IsInstanced.Value = true;
				this.instanceBinding = new ListBinding<Matrix, ModelInstance>(this.Instances, this.instances, x => new Matrix[] { x.transform });
				this.Add(this.instanceBinding);
			}

			public IPropertyBinding AddInstanceBoolParameter(string name)
			{
				IPropertyBinding binding = null;
				if (this.parameters.TryGetValue(name, out binding))
					return binding;
				
				binding = new Binding<bool[]>(this.GetBoolArrayParameter(name), () => this.instances.Select(x => x.getParameterValue<bool>(name)).ToArray());
				this.parameters.Add(name, binding);
				this.Add(binding);
				return binding;
			}

			public IPropertyBinding AddInstanceIntParameter(string name)
			{
				IPropertyBinding binding = null;
				if (this.parameters.TryGetValue(name, out binding))
					return binding;

				binding = new Binding<int[]>(this.GetIntArrayParameter(name), () => this.instances.Select(x => x.getParameterValue<int>(name)).ToArray());
				this.parameters.Add(name, binding);
				this.Add(binding);
				return binding;
			}

			public IPropertyBinding AddInstanceFloatParameter(string name)
			{
				IPropertyBinding binding = null;
				if (this.parameters.TryGetValue(name, out binding))
					return binding;

				binding = new Binding<float[]>(this.GetFloatArrayParameter(name), () => this.instances.Select(x => x.getParameterValue<float>(name)).ToArray());
				this.parameters.Add(name, binding);
				this.Add(binding);
				return binding;
			}

			public IPropertyBinding AddInstanceVector3Parameter(string name)
			{
				IPropertyBinding binding = null;
				if (this.parameters.TryGetValue(name, out binding))
					return binding;

				binding = new Binding<Vector3[]>(this.GetVector3ArrayParameter(name), () => this.instances.Select(x => x.getParameterValue<Vector3>(name)).ToArray());
				this.parameters.Add(name, binding);
				this.Add(binding);
				return binding;
			}

			public IPropertyBinding AddInstanceMatrixParameter(string name)
			{
				IPropertyBinding binding = null;
				if (this.parameters.TryGetValue(name, out binding))
					return binding;

				binding = new Binding<Matrix[]>(this.GetMatrixArrayParameter(name), () => this.instances.Select(x => x.getParameterValue<Matrix>(name)).ToArray());
				this.parameters.Add(name, binding);
				this.Add(binding);
				return binding;
			}

			public void Add(ModelInstance instance)
			{
				this.instances.Add(instance);
				foreach (IPropertyBinding parameter in this.parameters.Values)
					this.ParameterChanged(parameter);
			}

			public void Remove(ModelInstance instance)
			{
				this.instances.Remove(instance);
				if (this.instances.Count > 0)
				{
					foreach (IPropertyBinding parameter in this.parameters.Values)
						this.ParameterChanged(parameter);
				}
			}

			public void ParameterChanged(IPropertyBinding parameter)
			{
				if (!this.modifiedParameters.Contains(parameter))
					this.modifiedParameters.Add(parameter);
			}

			protected override void drawInstances(RenderParameters parameters, Matrix transform)
			{
				if (parameters.IsMainRender)
				{
					foreach (IPropertyBinding binding in this.modifiedParameters)
						binding.OnChanged(null);
					this.modifiedParameters.Clear();
					for (int i = 0; i < this.instances.Count; i++)
						this.instances.Changed(i, this.instances[i]);
				}
				base.drawInstances(parameters, transform);
			}

			protected override void delete()
			{
				base.delete();
				this.modifiedParameters.Clear();
				this.instanceBinding = null;
			}
		}

		public class ModelInstanceSystemAlpha : ModelInstanceSystem, IDrawableAlphaComponent
		{
			public Property<float> Alpha = null;

			public ModelInstanceSystemAlpha()
			{
				this.Alpha = this.GetFloatParameter("Alpha");
				this.Alpha.Value = 1.0f;
				this.Alpha.Editable = true;
			}

			public override void Draw(GameTime time, RenderParameters parameters)
			{

			}

			void IDrawableAlphaComponent.DrawAlpha(GameTime time, RenderParameters parameters)
			{
				if (this.Alpha > 0.0f)
					base.Draw(time, parameters);
			}

			protected override bool setParameters(Matrix transform, RenderParameters parameters)
			{
				bool result = base.setParameters(transform, parameters);
				if (result)
					this.effect.Parameters["DepthBuffer"].SetValue(parameters.DepthBuffer);
				return result;
			}
		}

		protected ModelInstanceSystem model;

		protected Dictionary<string, IProperty> parameters = new Dictionary<string, IProperty>();

		public Property<string> Filename = new Property<string> { Editable = true };
		
		public Property<Matrix> Transform = new Property<Matrix> { Editable = false, Value = Matrix.Identity };

		public Property<bool> EnableAlpha = new Property<bool> { Editable = true, Value = false };

		public Property<Vector3> Scale = new Property<Vector3> { Editable = false, Value = Vector3.One };

		protected Property<Matrix> transform = new Property<Matrix> { Editable = false };

		public Property<int> InstanceKey = new Property<int> { Editable = true };

		[XmlIgnore]
		public Property<string> FullInstanceKey = new Property<string> { Editable = false };

		[XmlArray("Parameters")]
		[XmlArrayItem("Parameter", Type = typeof(DictionaryEntry))]
		private DictionaryEntry[] deserializedParameters;
		public DictionaryEntry[] Parameters
		{
			get
			{
				return this.parameters.Where(x => x.Value.Serialize).Select(x => new DictionaryEntry(x.Key, x.Value)).ToArray();
			}
			set
			{
				this.deserializedParameters = value;
			}
		}

		[XmlIgnore]
		public bool IsFirstInstance;

		[XmlIgnore]
		public Model Model
		{
			get
			{
				return this.model;
			}
		}

		protected void refreshModel()
		{
			if (!string.IsNullOrEmpty(this.Filename.InternalValue))
			{
				string key = "InstanceSystem" + (this.EnableAlpha ? "Alpha" : "") + ":" + this.Filename.Value + "+" + this.InstanceKey.Value.ToString();

				Entity world = Lemma.Factories.WorldFactory.Get();
				
				ModelInstanceSystem newModel = this.EnableAlpha ? world.Get<ModelInstanceSystemAlpha>(key) : world.Get<ModelInstanceSystem>(key);

				bool foundExistingModel = newModel != null;

				if (!foundExistingModel)
				{
					newModel = this.EnableAlpha ? new ModelInstanceSystemAlpha { Editable = false } : new ModelInstanceSystem { Editable = false };
					newModel.Filename.Value = this.Filename.InternalValue;
					newModel.Key = key;
					world.Add(key, newModel);
				}

				if (newModel != this.model)
				{
					this.IsFirstInstance = !foundExistingModel;
					newModel.Add(this);
					if (this.model != null)
						this.model.Remove(this);
					this.model = newModel;
					this.parameters.Clear();
				}

				this.FullInstanceKey.Value = key;
			}
		}

		public void Setup(string filename, int instanceKey, bool alpha = false)
		{
			bool refresh = this.Filename.Value != filename;
			refresh |= this.InstanceKey.Value != instanceKey;
			if (refresh)
			{
				this.Filename.InternalValue = filename;
				this.InstanceKey.InternalValue = instanceKey;
				this.EnableAlpha.InternalValue = alpha;
				this.refreshModel();
				this.Filename.Changed();
				this.InstanceKey.Changed();
				this.EnableAlpha.Changed();
			}
		}

		public override void InitializeProperties()
		{
			this.Enabled.Editable = true;

			this.Filename.Set = delegate(string value)
			{
				this.Filename.InternalValue = value;
				this.refreshModel();
			};
			this.InstanceKey.Set = delegate(int value)
			{
				this.InstanceKey.InternalValue = value;
				this.refreshModel();
			};
			this.EnableAlpha.Set = delegate(bool value)
			{
				this.EnableAlpha.InternalValue = value;
				this.refreshModel();
			};

			this.Add(new Binding<Matrix>(this.transform, () => Matrix.CreateScale(this.Scale) * this.Transform, this.Scale, this.Transform));

			if (this.deserializedParameters != null)
			{
				for (int i = 0; i < this.deserializedParameters.Length; i++)
				{
					IProperty property = (IProperty)this.deserializedParameters[i].Value;
					PropertyInfo prop = property.GetType().GetProperty("Value");
					string name = (string)this.deserializedParameters[i].Key;

					if (prop.PropertyType.IsAssignableFrom(typeof(bool)))
						this.GetBoolParameter(name).Value = (bool)prop.GetValue(property, null);
					else if (prop.PropertyType.IsAssignableFrom(typeof(int)))
						this.GetIntParameter(name).Value = (int)prop.GetValue(property, null);
					else if (prop.PropertyType.IsAssignableFrom(typeof(float)))
						this.GetFloatParameter(name).Value = (float)prop.GetValue(property, null);
					else if (prop.PropertyType.IsAssignableFrom(typeof(Vector3)))
						this.GetVector3Parameter(name).Value = (Vector3)prop.GetValue(property, null);
					else if (prop.PropertyType.IsAssignableFrom(typeof(Matrix)))
						this.GetMatrixParameter(name).Value = (Matrix)prop.GetValue(property, null);
				}

				this.deserializedParameters = null;
			}
		}

		public Property<bool> GetBoolParameter(string name)
		{
			IProperty test = null;
			if (this.parameters.TryGetValue(name, out test))
				return (Property<bool>)test;

			Property<bool> result = new Property<bool> { Editable = false };
			this.parameters.Add(name, result);

			IPropertyBinding parameter = this.model.AddInstanceBoolParameter(name);
			result.Set = delegate(bool value)
			{
				result.InternalValue = value;
				this.model.ParameterChanged(parameter);
			};
			return result;
		}

		public Property<int> GetIntParameter(string name)
		{
			IProperty test = null;
			if (this.parameters.TryGetValue(name, out test))
				return (Property<int>)test;

			Property<int> result = new Property<int> { Editable = false };
			this.parameters.Add(name, result);

			IPropertyBinding parameter = this.model.AddInstanceIntParameter(name);
			result.Set = delegate(int value)
			{
				result.InternalValue = value;
				this.model.ParameterChanged(parameter);
			};
			return result;
		}

		public Property<float> GetFloatParameter(string name)
		{
			IProperty test = null;
			if (this.parameters.TryGetValue(name, out test))
				return (Property<float>)test;

			Property<float> result = new Property<float> { Editable = false };
			this.parameters.Add(name, result);

			IPropertyBinding parameter = this.model.AddInstanceFloatParameter(name);
			result.Set = delegate(float value)
			{
				result.InternalValue = value;
				this.model.ParameterChanged(parameter);
			};
			return result;
		}

		public Property<Vector3> GetVector3Parameter(string name)
		{
			IProperty test = null;
			if (this.parameters.TryGetValue(name, out test))
				return (Property<Vector3>)test;

			Property<Vector3> result = new Property<Vector3> { Editable = false };
			this.parameters.Add(name, result);

			IPropertyBinding parameter = this.model.AddInstanceVector3Parameter(name);
			result.Set = delegate(Vector3 value)
			{
				result.InternalValue = value;
				this.model.ParameterChanged(parameter);
			};
			return result;
		}

		public Property<Matrix> GetMatrixParameter(string name)
		{
			IProperty test = null;
			if (this.parameters.TryGetValue(name, out test))
				return (Property<Matrix>)test;

			Property<Matrix> result = new Property<Matrix> { Editable = false };
			this.parameters.Add(name, result);

			IPropertyBinding parameter = this.model.AddInstanceMatrixParameter(name);
			result.Set = delegate(Matrix value)
			{
				result.InternalValue = value;
				this.model.ParameterChanged(parameter);
			};
			return result;
		}

		protected override void delete()
		{
			base.delete();
			if (this.model != null)
			{
				this.model.Remove(this);
				this.model = null;
			}
		}

		protected T getParameterValue<T>(string name)
		{
			IProperty test = null;
			if (this.parameters.TryGetValue(name, out test))
				return ((Property<T>)test).Value;
			return default(T);
		}
	}
}
