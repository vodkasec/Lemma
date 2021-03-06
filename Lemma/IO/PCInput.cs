﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace Lemma.Components
{
	public class PCInput : Component, IUpdateableComponent
	{
		public enum MouseButton { None, LeftMouseButton, MiddleMouseButton, RightMouseButton }

		public enum InputState { Down, Up }

		public struct PCInputBinding
		{
			public Keys Key;
			public MouseButton MouseButton;
			public override string ToString()
			{
				if (this.Key != Keys.None)
				{
					switch (this.Key)
					{
						case Keys.D0:
							return "0";
						case Keys.D1:
							return "1";
						case Keys.D2:
							return "2";
						case Keys.D3:
							return "3";
						case Keys.D4:
							return "4";
						case Keys.D5:
							return "5";
						case Keys.D6:
							return "6";
						case Keys.D7:
							return "7";
						case Keys.D8:
							return "8";
						case Keys.D9:
							return "9";
						default:
							return this.Key.ToString();
					}
				}
				else if (this.MouseButton != PCInput.MouseButton.None)
					return this.MouseButton.ToString();
				else
					return "[?]";
			}
		}

		public Property<Vector2> Mouse = new Property<Vector2> { };
		public Property<int> ScrollWheel = new Property<int> { };
		public Property<bool> LeftMouseButton = new Property<bool> { };
		public Property<bool> MiddleMouseButton = new Property<bool> { };
		public Property<bool> RightMouseButton = new Property<bool> { };

		public Command LeftMouseButtonDown = new Command();
		public Command MiddleMouseButtonDown = new Command();
		public Command RightMouseButtonDown = new Command();
		public Command LeftMouseButtonUp = new Command();
		public Command MiddleMouseButtonUp = new Command();
		public Command RightMouseButtonUp = new Command();
		public Command<int> MouseScrolled = new Command<int>();

		public void Bind(Property<PCInput.PCInputBinding> inputBinding, InputState state, Action action)
		{
			CommandBinding commandBinding = null;
			Action rebindCommand = delegate()
			{
				if (commandBinding != null)
					this.Remove(commandBinding);

				PCInput.PCInputBinding ib = inputBinding;
				if (ib.Key == Keys.None && ib.MouseButton == PCInput.MouseButton.None)
					commandBinding = null;
				else
				{
					commandBinding = new CommandBinding(state == InputState.Up ? this.GetInputUp(inputBinding) : this.GetInputDown(inputBinding), action);
					this.Add(commandBinding);
				}
			};
			this.Add(new NotifyBinding(rebindCommand, inputBinding));
			rebindCommand();
		}

		public void Bind(Property<PCInput.PCInputBinding> inputBinding, Property<bool> target)
		{
			Binding<bool> binding = null;
			Action rebind = delegate()
			{
				if (binding != null)
					this.Remove(binding);

				PCInput.PCInputBinding ib = inputBinding;
				if (ib.Key == Keys.None && ib.MouseButton == PCInput.MouseButton.None)
					binding = null;
				else
				{
					binding = new Binding<bool>(target, this.GetInput(ib));
					this.Add(binding);
				}
			};
			this.Add(new NotifyBinding(rebind, inputBinding));
			rebind();
		}

		protected Dictionary<Keys, Property<bool>> keyProperties = new Dictionary<Keys, Property<bool>>();

		protected Dictionary<Keys, Command> keyUpCommands = new Dictionary<Keys, Command>();
		protected Dictionary<Keys, Command> keyDownCommands = new Dictionary<Keys, Command>();

		protected List<Action<PCInput.PCInputBinding>> nextInputListeners = new List<Action<PCInputBinding>>();

		public struct Chord
		{
			public Keys Modifier, Key;
		}

		protected Dictionary<Chord, Command> chords = new Dictionary<Chord, Command>();

		protected bool chordActivated = false;

		public PCInput()
		{
			this.Editable = false;
			this.Serialize = false;
		}

		public override Entity Entity
		{
			get
			{
				return base.Entity;
			}
			set
			{
				base.Entity = value;
				this.EnabledWhenPaused.Value = false;
			}
		}

		private bool preventKeyDownEvents = false;

		public override void InitializeProperties()
		{
			this.Add(new CommandBinding(this.OnDisabled, delegate()
			{
				// Release all the keys
				foreach (KeyValuePair<Keys, Property<bool>> pair in this.keyProperties)
				{
					if (pair.Value.Value)
					{
						Command command;
						if (keyUpCommands.TryGetValue(pair.Key, out command))
							command.Execute();
						pair.Value.Value = false;
					}
				}

				this.chordActivated = false;

				// Release mouse buttons
				if (this.LeftMouseButton)
				{
					this.LeftMouseButton.Value = false;
					this.LeftMouseButtonUp.Execute();
				}

				if (this.RightMouseButton)
				{
					this.RightMouseButton.Value = false;
					this.RightMouseButtonUp.Execute();
				}

				if (this.MiddleMouseButton)
				{
					this.MiddleMouseButton.Value = false;
					this.MiddleMouseButtonUp.Execute();
				}
			}));

			this.Add(new CommandBinding(this.OnEnabled, delegate()
			{
				// Don't send key-down events for the first frame after we're enabled.
				this.preventKeyDownEvents = true;
			}));
		}

		public Property<bool> GetKey(Keys key)
		{
			if (this.keyProperties.ContainsKey(key))
				return this.keyProperties[key];
			else
			{
				Property<bool> newProperty = new Property<bool> { };
				this.keyProperties.Add(key, newProperty);
				return newProperty;
			}
		}

		public Command GetKeyDown(Keys key)
		{
			if (this.keyDownCommands.ContainsKey(key))
				return this.keyDownCommands[key];
			else
			{
				this.GetKey(key);
				Command command = new Command();
				this.keyDownCommands.Add(key, command);
				return command;
			}
		}

		public void GetNextInput(Action<PCInput.PCInputBinding> listener)
		{
			this.nextInputListeners.Add(listener);
		}

		public Command GetInputDown(PCInputBinding binding)
		{
			if (binding.Key != Keys.None)
				return this.GetKeyDown(binding.Key);
			else
			{
				switch (binding.MouseButton)
				{
					case MouseButton.LeftMouseButton:
						return this.LeftMouseButtonDown;
					case MouseButton.MiddleMouseButton:
						return this.MiddleMouseButtonDown;
					case MouseButton.RightMouseButton:
						return this.RightMouseButtonDown;
					default:
						return null;
				}
			}
		}

		public Property<bool> GetInput(PCInputBinding binding)
		{
			if (binding.Key != Keys.None)
				return this.GetKey(binding.Key);
			else
			{
				switch (binding.MouseButton)
				{
					case MouseButton.LeftMouseButton:
						return this.LeftMouseButton;
					case MouseButton.MiddleMouseButton:
						return this.MiddleMouseButton;
					case MouseButton.RightMouseButton:
						return this.RightMouseButton;
					default:
						return null;
				}
			}
		}

		public Command GetInputUp(PCInputBinding binding)
		{
			if (binding.Key != Keys.None)
				return this.GetKeyUp(binding.Key);
			else
			{
				switch (binding.MouseButton)
				{
					case MouseButton.LeftMouseButton:
						return this.LeftMouseButtonUp;
					case MouseButton.MiddleMouseButton:
						return this.MiddleMouseButtonUp;
					case MouseButton.RightMouseButton:
						return this.RightMouseButtonUp;
					default:
						return null;
				}
			}
		}

		public Command GetKeyUp(Keys key)
		{
			if (this.keyUpCommands.ContainsKey(key))
				return this.keyUpCommands[key];
			else
			{
				this.GetKey(key);
				Command command = new Command();
				this.keyUpCommands.Add(key, command);
				return command;
			}
		}

		public Command GetChord(Chord chord)
		{
			if (this.chords.ContainsKey(chord))
				return this.chords[chord];
			else
			{
				Command cmd = new Command();
				this.chords.Add(chord, cmd);
				return cmd;
			}
		}

		private void notifyNextInputListeners(PCInput.PCInputBinding input)
		{
			foreach (Action<PCInput.PCInputBinding> listener in this.nextInputListeners)
				listener(input);
			this.nextInputListeners.Clear();
			this.preventKeyDownEvents = true;
		}

		public virtual void Update(float elapsedTime)
		{
			if (!main.IsActive)
				return;

			KeyboardState keyboard = this.main.KeyboardState;

			Keys[] keys = keyboard.GetPressedKeys();
			if (keys.Length > 0 && this.nextInputListeners.Count > 0)
				this.notifyNextInputListeners(new PCInputBinding { Key = keys[0] });

			foreach (KeyValuePair<Keys, Property<bool>> pair in this.keyProperties)
			{
				bool newValue = keyboard.IsKeyDown(pair.Key);
				if (newValue != pair.Value.Value)
				{
					pair.Value.Value = newValue;
					if (!this.preventKeyDownEvents)
					{
						if (newValue)
						{
							Command command;
							if (keyDownCommands.TryGetValue(pair.Key, out command))
								command.Execute();
						}
						else
						{
							Command command;
							if (keyUpCommands.TryGetValue(pair.Key, out command))
								command.Execute();
						}
					}
				}
			}

			if (!this.chordActivated && !this.preventKeyDownEvents)
			{
				if (keys.Length == 2)
				{
					Chord chord = new Chord();
					if (keys[1] == Keys.LeftAlt || keys[1] == Keys.LeftControl || keys[1] == Keys.LeftShift || keys[1] == Keys.LeftWindows
						|| keys[1] == Keys.RightAlt || keys[1] == Keys.RightControl || keys[1] == Keys.RightShift || keys[1] == Keys.RightWindows)
					{
						chord.Modifier = keys[1];
						chord.Key = keys[0];
					}
					else
					{
						chord.Modifier = keys[0];
						chord.Key = keys[1];
					}
					if (this.chords.ContainsKey(chord))
					{
						this.chords[chord].Execute();
						this.chordActivated = true;
					}
				}
			}
			else if (keyboard.GetPressedKeys().Length == 0)
				this.chordActivated = false;

			MouseState mouse = this.main.MouseState;
			this.handleMouse();

			bool newLeftMouseButton = mouse.LeftButton == ButtonState.Pressed;
			if (newLeftMouseButton != this.LeftMouseButton)
			{
				this.LeftMouseButton.Value = newLeftMouseButton;
				if (!this.preventKeyDownEvents)
				{
					if (newLeftMouseButton)
					{
						if (this.nextInputListeners.Count > 0)
							this.notifyNextInputListeners(new PCInputBinding { MouseButton = MouseButton.LeftMouseButton });
						this.LeftMouseButtonDown.Execute();
					}
					else
						this.LeftMouseButtonUp.Execute();
				}
			}

			bool newMiddleMouseButton = mouse.MiddleButton == ButtonState.Pressed;
			if (newMiddleMouseButton != this.MiddleMouseButton)
			{
				this.MiddleMouseButton.Value = newMiddleMouseButton;
				if (!this.preventKeyDownEvents)
				{
					if (newMiddleMouseButton)
					{
						if (this.nextInputListeners.Count > 0)
							this.notifyNextInputListeners(new PCInputBinding { MouseButton = MouseButton.MiddleMouseButton });
						this.MiddleMouseButtonDown.Execute();
					}
					else
						this.MiddleMouseButtonUp.Execute();
				}
			}

			bool newRightMouseButton = mouse.RightButton == ButtonState.Pressed;
			if (newRightMouseButton != this.RightMouseButton)
			{
				this.RightMouseButton.Value = newRightMouseButton;
				if (!this.preventKeyDownEvents)
				{
					if (newRightMouseButton)
					{
						if (this.nextInputListeners.Count > 0)
							this.notifyNextInputListeners(new PCInputBinding { MouseButton = MouseButton.RightMouseButton });
						this.RightMouseButtonDown.Execute();
					}
					else
						this.RightMouseButtonUp.Execute();
				}
			}

			int newScrollWheel = mouse.ScrollWheelValue;
			int oldScrollWheel = this.ScrollWheel;
			if (newScrollWheel != oldScrollWheel)
			{
				this.ScrollWheel.Value = newScrollWheel;
				if (!this.preventKeyDownEvents)
					this.MouseScrolled.Execute(newScrollWheel > oldScrollWheel ? 1 : -1);
			}
			this.preventKeyDownEvents = false;
		}

		protected virtual void handleMouse()
		{
			MouseState state = this.main.MouseState, lastState = this.main.LastMouseState;
			if (state.X != lastState.X || state.Y != lastState.Y)
				this.Mouse.Value = new Vector2(state.X, state.Y);
		}
	}
}