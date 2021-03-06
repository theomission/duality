﻿using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Xml.Linq;
using System.Windows.Forms;

using Duality;
using Duality.Components;
using Duality.Resources;
using Duality.Drawing;

using Duality.Editor;
using Duality.Editor.Forms;
using Duality.Editor.Plugins.CamView.Properties;
using Duality.Editor.Plugins.CamView.UndoRedoActions;

namespace Duality.Editor.Plugins.CamView.CamViewStates
{
	public abstract class CamViewState : CamViewClient, IHelpProvider
	{
		[Flags]
		public enum UserGuideType
		{
			None		= 0x0,

			Position	= 0x1,
			Scale		= 0x2,

			All			= Position | Scale
		}
		public enum LockedAxis
		{
			None,

			X,
			Y,
			Z
		}
		public enum CameraAction
		{
			None,
			Move,
			Rotate,

			// Alternate movement (Spacebar pressed)
			DragScene,
			RotateScene
		}
		public enum ObjectAction
		{
			None,
			RectSelect,
			Move,
			Rotate,
			Scale,
		}
		public abstract class SelObj : IEquatable<SelObj>
		{
			public abstract object ActualObject { get; }
			public abstract bool HasTransform { get; }
			public abstract float BoundRadius { get; }
			public abstract Vector3 Pos { get; set; }
			public virtual Vector3 Scale
			{
				get { return Vector3.One; }
				set {}
			}
			public virtual float Angle
			{
				get { return 0.0f; }
				set {}
			}
			public virtual bool ShowBoundRadius
			{
				get { return true; }
			}
			public virtual bool ShowPos
			{
				get { return true; }
			}
			public virtual bool ShowAngle
			{
				get { return false; }
			}
			public virtual string DisplayObjectName
			{
				get { return this.ActualObject != null ? this.ActualObject.ToString() : "null"; }
			}
			public bool IsInvalid
			{
				get { return this.ActualObject == null; }
			}

			public virtual bool IsActionAvailable(ObjectAction action)
			{
				if (action == ObjectAction.Move) return true;
				return false;
			}
			public virtual string UpdateActionText(ObjectAction action, bool performing)
			{
				return null;
			}
			
			public override bool Equals(object obj)
			{
				if (obj is SelObj)
					return this == (SelObj)obj;
				else
					return base.Equals(obj);
			}
			public override int GetHashCode()
			{
				return this.ActualObject.GetHashCode();
			}
			public bool Equals(SelObj other)
			{
				return this == other;
			}

			public static bool operator ==(SelObj first, SelObj second)
			{
				if (object.ReferenceEquals(first, null))
				{
					if (object.ReferenceEquals(second, null)) return true;
					else return false;
				}
				else if (object.ReferenceEquals(second, null))
					return false;

				return first.ActualObject == second.ActualObject;
			}
			public static bool operator !=(SelObj first, SelObj second)
			{
				return !(first == second);
			}
		}


		private static readonly ContentRef<Duality.Resources.Font> OverlayFont = Duality.Resources.Font.GenericMonospace8;

		private Vector3			camVel					= Vector3.Zero;
		private	float			camAngleVel				= 0.0f;
		private	Point			camActionBeginLoc		= Point.Empty;
		private Vector3			camActionBeginLocSpace	= Vector3.Zero;
		private	CameraAction	camAction				= CameraAction.None;
		private	bool			camActionAllowed		= true;
		private	bool			camTransformChanged		= false;
		private	bool			camBeginDragScene		= false;
		private	Camera.Pass		camPassBg			= null;
		private	Camera.Pass		camPassEdWorld		= null;
		private Camera.Pass		camPassEdScreen		= null;
		private	bool			engineUserInput		= false;
		private	bool			actionAllowed		= true;
		private	bool			actionIsClone		= false;
		private	Point			actionBeginLoc		= Point.Empty;
		private Vector3			actionBeginLocSpace	= Vector3.Zero;
		private Vector3			actionLastLocSpace	= Vector3.Zero;
		private	LockedAxis		actionLockedAxis	= LockedAxis.None;
		private	UserGuideType	snapToUserGuides	= UserGuideType.All;
		private ObjectAction	action				= ObjectAction.None;
		private	bool			selectionStatsValid	= false;
		private	Vector3			selectionCenter		= Vector3.Zero;
		private	float			selectionRadius		= 0.0f;
		private	ObjectSelection	activeRectSel		= new ObjectSelection();
		private	ObjectAction	mouseoverAction		= ObjectAction.None;
		private	SelObj			mouseoverObject		= null;
		private	bool			mouseoverSelect		= false;
		private	bool			mouseover			= false;
		private	CameraAction	drawCamGizmoState	= CameraAction.None;
		private	ObjectAction	drawSelGizmoState	= ObjectAction.None;
		private	FormattedText	statusText			= new FormattedText();
		private	FormattedText	actionText			= new FormattedText();
		private	List<Type>		lastActiveLayers	= new List<Type>();
		private	List<Type>		lastObjVisibility	= new List<Type>();
		protected	List<SelObj>	actionObjSel	= new List<SelObj>();
		protected	List<SelObj>	allObjSel		= new List<SelObj>();
		protected	List<SelObj>	indirectObjSel	= new List<SelObj>();


		public ObjectAction SelObjAction
		{
			get { return this.action; }
		}
		public abstract string StateName { get; }

		public IEnumerable<SelObj> SelectedObjects
		{
			get { return this.allObjSel; }
		}
		public bool IsActive
		{
			get { return this.View != null && this.View.ActiveState == this; }
		}
		public bool EngineUserInput
		{
			get { return this.engineUserInput; }
			protected set { this.engineUserInput = value; }
		}
		public bool MouseActionAllowed
		{
			get { return this.actionAllowed; }
			protected set
			{
				this.actionAllowed = value;
				if (!this.actionAllowed)
				{
					this.mouseoverAction = ObjectAction.None;
					this.mouseoverObject = null;
					this.mouseoverSelect = false;
					if (this.action != ObjectAction.None)
					{
						this.EndAction();
						this.UpdateAction();
					}
				}
			}
		}
		public bool CameraActionAllowed
		{
			get { return this.camActionAllowed; }
			protected set
			{ 
				this.camActionAllowed = value;
				if (!this.camActionAllowed && this.camAction != CameraAction.None)
				{
					this.camAction = CameraAction.None;
					this.Invalidate();
				}
			}
		}
		public bool Mouseover
		{
			get { return this.mouseover; }
		}
		public SelObj MouseoverObject
		{
			get { return this.mouseoverObject; }
		}
		public CameraAction CamAction
		{
			get { return this.camAction; }
		}
		public ObjectAction MouseoverAction
		{
			get { return this.mouseoverAction; }
		}
		public ObjectAction Action
		{
			get { return this.action; }
		}
		public ObjectAction VisibleAction
		{
			get
			{
				return 
					(this.drawSelGizmoState != ObjectAction.None ? this.drawSelGizmoState : 
					(this.action != ObjectAction.None ? this.action :
					(this.mouseoverAction != ObjectAction.RectSelect ? this.mouseoverAction :
					ObjectAction.None)));
			}
		}
		public UserGuideType SnapToUserGuides
		{
			get { return this.snapToUserGuides; }
			protected set { this.snapToUserGuides = value; }
		}
		public string StatusText
		{
			get { return this.statusText.SourceText; }
		}
		public string ActionText
		{
			get { return this.actionText.SourceText; }
		}

		public Vector3 SelectionCenter
		{
			get { return selectionCenter; }
		}


		internal protected virtual void OnEnterState()
		{
			this.RestoreActiveLayers();
			this.RestoreObjectVisibility();

			// Create re-usable render passes for editor gizmos
			this.camPassBg = new Camera.Pass();
			this.camPassBg.MatrixMode = RenderMatrix.OrthoScreen;
			this.camPassBg.ClearFlags = ClearFlag.None;
			this.camPassBg.VisibilityMask = VisibilityFlag.ScreenOverlay;
			this.camPassEdWorld = new Camera.Pass();
			this.camPassEdWorld.ClearFlags = ClearFlag.None;
			this.camPassEdWorld.VisibilityMask = VisibilityFlag.None;
			this.camPassEdScreen = new Camera.Pass();
			this.camPassEdScreen.MatrixMode = RenderMatrix.OrthoScreen;
			this.camPassEdScreen.ClearFlags = ClearFlag.None;
			this.camPassEdScreen.VisibilityMask = VisibilityFlag.ScreenOverlay;

			this.camPassBg.CollectDrawcalls			+= this.camPassBg_CollectDrawcalls;
			this.camPassEdWorld.CollectDrawcalls	+= this.camPassEdWorld_CollectDrawcalls;
			this.camPassEdScreen.CollectDrawcalls	+= this.camPassEdScreen_CollectDrawcalls;

			Control control = this.RenderableSite.Control;
			control.Paint		+= this.RenderableControl_Paint;
			control.MouseDown	+= this.RenderableControl_MouseDown;
			control.MouseUp		+= this.RenderableControl_MouseUp;
			control.MouseMove	+= this.RenderableControl_MouseMove;
			control.MouseWheel	+= this.RenderableControl_MouseWheel;
			control.MouseLeave	+= this.RenderableControl_MouseLeave;
			control.KeyDown		+= this.RenderableControl_KeyDown;
			control.KeyUp		+= this.RenderableControl_KeyUp;
			control.GotFocus	+= this.RenderableControl_GotFocus;
			control.LostFocus	+= this.RenderableControl_LostFocus;
			control.DragDrop	+= this.RenderableControl_DragDrop;
			control.DragEnter	+= this.RenderableControl_DragEnter;
			control.DragLeave	+= this.RenderableControl_DragLeave;
			control.DragOver	+= this.RenderableControl_DragOver;
			control.Resize		+= this.RenderableControl_Resize;
			this.View.PerspectiveChanged	+= this.View_FocusDistChanged;
			this.View.CurrentCameraChanged	+= this.View_CurrentCameraChanged;
			DualityEditorApp.UpdatingEngine += this.DualityEditorApp_UpdatingEngine;
			DualityEditorApp.ObjectPropertyChanged += this.DualityEditorApp_ObjectPropertyChanged;

			Scene.Leaving += this.Scene_Changed;
			Scene.Entered += this.Scene_Changed;
			Scene.GameObjectParentChanged += this.Scene_Changed;
			Scene.GameObjectAdded += this.Scene_Changed;
			Scene.GameObjectRemoved += this.Scene_Changed;
			Scene.ComponentAdded += this.Scene_Changed;
			Scene.ComponentRemoving += this.Scene_Changed;

			if (Scene.Current != null) this.Scene_Changed(this, EventArgs.Empty);
			
			// Initial Camera update
			this.OnCurrentCameraChanged(new CamView.CameraChangedEventArgs(null, this.CameraComponent));
			this.UpdateFormattedTextRenderers();
		}
		internal protected virtual void OnLeaveState() 
		{
			this.Cursor = CursorHelper.Arrow;

			Control control = this.RenderableSite.Control;
			control.Paint		-= this.RenderableControl_Paint;
			control.MouseDown	-= this.RenderableControl_MouseDown;
			control.MouseUp		-= this.RenderableControl_MouseUp;
			control.MouseMove	-= this.RenderableControl_MouseMove;
			control.MouseWheel	-= this.RenderableControl_MouseWheel;
			control.MouseLeave	-= this.RenderableControl_MouseLeave;
			control.KeyDown		-= this.RenderableControl_KeyDown;
			control.KeyUp		-= this.RenderableControl_KeyUp;
			control.LostFocus	-= this.RenderableControl_LostFocus;
			control.DragDrop	-= this.RenderableControl_DragDrop;
			control.DragEnter	-= this.RenderableControl_DragEnter;
			control.DragLeave	-= this.RenderableControl_DragLeave;
			control.DragOver	-= this.RenderableControl_DragOver;
			control.Resize		-= this.RenderableControl_Resize;
			this.View.PerspectiveChanged			-= this.View_FocusDistChanged;
			this.View.CurrentCameraChanged			-= this.View_CurrentCameraChanged;
			DualityEditorApp.UpdatingEngine			-= this.DualityEditorApp_UpdatingEngine;
			DualityEditorApp.ObjectPropertyChanged	-= this.DualityEditorApp_ObjectPropertyChanged;
			
			Scene.Leaving -= this.Scene_Changed;
			Scene.Entered -= this.Scene_Changed;
			Scene.GameObjectParentChanged -= this.Scene_Changed;
			Scene.GameObjectAdded -= this.Scene_Changed;
			Scene.GameObjectRemoved -= this.Scene_Changed;
			Scene.ComponentAdded -= this.Scene_Changed;
			Scene.ComponentRemoving -= this.Scene_Changed;

			this.SaveActiveLayers();
			this.SaveObjectVisibility();

			// Final Camera cleanup
			this.OnCurrentCameraChanged(new CamView.CameraChangedEventArgs(this.CameraComponent, null));
		}
		
		internal protected virtual void SaveUserData(XElement node)
		{
			if (this.IsActive) this.SaveActiveLayers();
			if (this.IsActive) this.SaveObjectVisibility();

			XElement activeLayersNode = new XElement("ActiveLayers");
			foreach (Type t in this.lastActiveLayers)
			{
				XElement typeEntry = new XElement("Item", t.GetTypeId());
				activeLayersNode.Add(typeEntry);
			}
			if (!activeLayersNode.IsEmpty)
				node.Add(activeLayersNode);

			XElement objVisibilityNode = new XElement("ObjectVisibility");
			foreach (Type t in this.lastObjVisibility)
			{
				XElement typeEntry = new XElement("Item", t.GetTypeId());
				objVisibilityNode.Add(typeEntry);
			}
			if (!objVisibilityNode.IsEmpty)
				node.Add(objVisibilityNode);
		}
		internal protected virtual void LoadUserData(XElement node)
		{
			XElement activeLayersNode = node.Element("ActiveLayers");
			if (activeLayersNode != null)
			{
				this.lastActiveLayers.Clear();
				foreach (XElement layerNode in activeLayersNode.Elements("Item"))
				{
					Type layerType = ReflectionHelper.ResolveType(layerNode.Value);
					if (layerType != null) this.lastActiveLayers.Add(layerType);
				}
			}
			
			XElement objVisibilityNode = node.Element("ObjectVisibility");
			if (objVisibilityNode != null)
			{
				this.lastObjVisibility.Clear();
				foreach (XElement typeNode in objVisibilityNode.Elements("Item"))
				{
					Type type = ReflectionHelper.ResolveType(typeNode.Value);
					if (type != null) this.lastObjVisibility.Add(type);
				}
			}

			if (this.IsActive) this.RestoreActiveLayers();
			if (this.IsActive) this.RestoreObjectVisibility();
		}

		protected virtual void OnCollectStateDrawcalls(Canvas canvas)
		{
			// Assure we know how to display the current selection
			this.ValidateSelectionStats();

			// Collect the views layer drawcalls
			this.CollectLayerDrawcalls(canvas);

			List<SelObj> transformObjSel = this.allObjSel.Where(s => s.HasTransform).ToList();
			Point cursorPos = this.PointToClient(Cursor.Position);
			canvas.PushState();
			
			// Draw indirectly selected object overlay
			canvas.State.SetMaterial(new BatchInfo(DrawTechnique.Solid, ColorRgba.Lerp(this.FgColor, this.BgColor, 0.75f)));
			this.DrawSelectionMarkers(canvas, this.indirectObjSel);
			if (this.mouseoverObject != null && (this.mouseoverAction == ObjectAction.RectSelect || this.mouseoverSelect) && !transformObjSel.Contains(this.mouseoverObject)) 
				this.DrawSelectionMarkers(canvas, new [] { this.mouseoverObject });

			// Draw selected object overlay
			canvas.State.SetMaterial(new BatchInfo(DrawTechnique.Solid, this.FgColor));
			this.DrawSelectionMarkers(canvas, transformObjSel);

			// Draw overall selection boundary
			if (transformObjSel.Count > 1)
			{
				float midZ = transformObjSel.Average(t => t.Pos.Z);
				float maxZDiff = transformObjSel.Max(t => MathF.Abs(t.Pos.Z - midZ));
				if (maxZDiff > 0.001f)
				{
					canvas.State.SetMaterial(new BatchInfo(DrawTechnique.Solid, ColorRgba.Lerp(this.FgColor, this.BgColor, 0.5f)));
					canvas.DrawSphere(
						this.selectionCenter.X, 
						this.selectionCenter.Y, 
						this.selectionCenter.Z - 0.1f, 
						this.selectionRadius);
				}
				else
				{
					canvas.State.SetMaterial(new BatchInfo(DrawTechnique.Solid, ColorRgba.Lerp(this.FgColor, this.BgColor, 0.5f)));
					canvas.DrawCircle(
						this.selectionCenter.X, 
						this.selectionCenter.Y, 
						this.selectionCenter.Z - 0.1f, 
						this.selectionRadius);
				}
			}

			// Draw scale action dots
			bool canMove = this.actionObjSel.Any(s => s.IsActionAvailable(ObjectAction.Move));
			bool canScale = (canMove && this.actionObjSel.Count > 1) || this.actionObjSel.Any(s => s.IsActionAvailable(ObjectAction.Scale));
			if (canScale)
			{
				float dotR = 3.0f / this.GetScaleAtZ(this.selectionCenter.Z);
				canvas.State.SetMaterial(new BatchInfo(DrawTechnique.Solid, this.FgColor));
				canvas.FillCircle(
					this.selectionCenter.X + this.selectionRadius, 
					this.selectionCenter.Y, 
					this.selectionCenter.Z - 0.1f,
					dotR);
				canvas.FillCircle(
					this.selectionCenter.X - this.selectionRadius, 
					this.selectionCenter.Y, 
					this.selectionCenter.Z - 0.1f,
					dotR);
				canvas.FillCircle(
					this.selectionCenter.X, 
					this.selectionCenter.Y + this.selectionRadius, 
					this.selectionCenter.Z - 0.1f,
					dotR);
				canvas.FillCircle(
					this.selectionCenter.X, 
					this.selectionCenter.Y - this.selectionRadius, 
					this.selectionCenter.Z - 0.1f,
					dotR);
			}

			if (this.action != ObjectAction.None)
			{
				// Draw action lock axes
				this.DrawLockedAxes(canvas, this.selectionCenter.X, this.selectionCenter.Y, this.selectionCenter.Z, this.selectionRadius * 4);
			}

			canvas.PopState();
		}
		protected virtual void OnCollectStateOverlayDrawcalls(Canvas canvas)
		{
			// Gather general data
			Point cursorPos = this.PointToClient(Cursor.Position);
			ObjectAction visibleObjectAction = this.VisibleAction;

			// Update action text from hovered / selection / action object
			bool actionTextUpdated = false;
			Vector2 actionTextPos = new Vector2(cursorPos.X + 30, cursorPos.Y + 10);
			if (visibleObjectAction != ObjectAction.None && ((this.mouseoverObject != null && this.mouseoverSelect) || this.actionObjSel.Count == 1))
			{
				SelObj obj;
				if (this.mouseoverObject != null || this.mouseoverAction == visibleObjectAction)
				{
					obj = (this.mouseoverObject != null && this.mouseoverSelect) ? this.mouseoverObject : this.actionObjSel[0];
				}
				else
				{
					obj = this.actionObjSel[0];
					actionTextPos = this.GetScreenCoord(this.actionObjSel[0].Pos).Xy;
				}

				// If the SelObj is valid, draw the gizmo
				if (obj.ActualObject != null)
				{
					actionTextUpdated = true;
					this.actionText.SourceText = obj.UpdateActionText(visibleObjectAction, this.action != ObjectAction.None) ?? this.UpdateActionText();
				}
			}
			if (!actionTextUpdated)
				this.actionText.SourceText = this.UpdateActionText();

			// Collect the views overlay layer drawcalls
			this.CollectLayerOverlayDrawcalls(canvas);

			// Collect the states overlay drawcalls
			canvas.PushState();
			{
				// Draw camera movement indicators
				if (this.camAction != CameraAction.None)
				{
					canvas.PushState();
					canvas.State.ColorTint = ColorRgba.White.WithAlpha(0.5f);
					if (this.camAction == CameraAction.DragScene)
					{
						// Don't draw anything.
					}
					else if (this.camAction == CameraAction.RotateScene)
					{
						canvas.FillCircle(this.camActionBeginLoc.X, this.camActionBeginLoc.Y, 3);
						canvas.DrawLine(this.camActionBeginLoc.X, this.camActionBeginLoc.Y, cursorPos.X, this.camActionBeginLoc.Y);
					}
					else if (this.camAction == CameraAction.Move)
					{
						canvas.FillCircle(this.camActionBeginLoc.X, this.camActionBeginLoc.Y, 3);
						canvas.DrawLine(this.camActionBeginLoc.X, this.camActionBeginLoc.Y, cursorPos.X, cursorPos.Y);
					}
					else if (this.camAction == CameraAction.Rotate)
					{
						canvas.FillCircle(this.camActionBeginLoc.X, this.camActionBeginLoc.Y, 3);
						canvas.DrawLine(this.camActionBeginLoc.X, this.camActionBeginLoc.Y, cursorPos.X, this.camActionBeginLoc.Y);
					}
					canvas.PopState();
				}
				
				// Normalize action text position
				if (this.actionText.Fonts != null && this.actionText.Fonts.Any(r => r.IsAvailable && r.Res.IsPixelGridAligned))
				{
					actionTextPos.X = MathF.Round(actionTextPos.X);
					actionTextPos.Y = MathF.Round(actionTextPos.Y);
				}

				// Draw current action text
				if (!this.actionText.IsEmpty)
				{
					canvas.DrawText(this.actionText, actionTextPos.X, actionTextPos.Y, drawBackground: true);
				}

				// Update / Draw current status text
				{
					this.statusText.SourceText = this.UpdateStatusText();
					if (!this.statusText.IsEmpty)
					{
						Vector2 statusTextSize = this.statusText.Size;
						canvas.DrawText(this.statusText, 10, this.ClientSize.Height - statusTextSize.Y - 10, drawBackground: true);
					}
				}

				// Draw rect selection
				if (this.action == ObjectAction.RectSelect)
					canvas.DrawRect(this.actionBeginLoc.X, this.actionBeginLoc.Y, cursorPos.X - this.actionBeginLoc.X, cursorPos.Y - this.actionBeginLoc.Y);
			}
			canvas.PopState();
		}
		protected virtual void OnCollectStateBackgroundDrawcalls(Canvas canvas)
		{
			// Collect the views overlay layer drawcalls
			this.CollectLayerBackgroundDrawcalls(canvas);
		}
		protected virtual string UpdateStatusText()
		{
			CameraAction visibleCamAction = this.drawCamGizmoState != CameraAction.None ? this.drawCamGizmoState : this.camAction;
			ObjectAction visibleObjectAction = this.VisibleAction;

			// Draw camera action hints
			if (visibleCamAction == CameraAction.Rotate || visibleCamAction == CameraAction.RotateScene)
			{
				return string.Format("Cam Angle: {0,3:0}°", MathF.RadToDeg(this.CameraObj.Transform.Angle));
			}
			else if (visibleCamAction == CameraAction.Move || visibleCamAction == CameraAction.DragScene || this.camVel.Z != 0.0f)
			{
				if (visibleCamAction == CameraAction.Move || visibleCamAction == CameraAction.DragScene)
				{
					return
						string.Format("Cam X:{0,7:0}/n", this.CameraObj.Transform.Pos.X) +
						string.Format("Cam Y:{0,7:0}/n", this.CameraObj.Transform.Pos.Y) +
						string.Format("Cam Z:{0,7:0}", this.CameraObj.Transform.Pos.Z);
				}
				else if (this.camVel.Z != 0.0f)
				{
					return string.Format("Cam Z:{0,7:0}", this.CameraObj.Transform.Pos.Z);
				}
			}

			// Draw action hints
			if (visibleObjectAction == ObjectAction.Move)				return Properties.CamViewRes.CamView_Action_Move;
			else if (visibleObjectAction == ObjectAction.Rotate)		return Properties.CamViewRes.CamView_Action_Rotate;
			else if (visibleObjectAction == ObjectAction.Scale)			return Properties.CamViewRes.CamView_Action_Scale;
			else if (visibleObjectAction == ObjectAction.RectSelect)	return Properties.CamViewRes.CamView_Action_Select_Active;

			// Unhandled
			return null;
		}
		protected virtual string UpdateActionText()
		{
			return null;
		}
		protected virtual void OnRenderState()
		{
			// Render CamView
			this.CameraComponent.Render(new Rect(this.ClientSize.Width, this.ClientSize.Height));
		}
		protected virtual void OnUpdateState()
		{
			Camera cam = this.CameraComponent;
			GameObject camObj = this.CameraObj;
			Point cursorPos = this.PointToClient(Cursor.Position);

			float unscaledTimeMult = Time.TimeMult / Time.TimeScale;

			this.camTransformChanged = false;
			
			if (this.camAction == CameraAction.DragScene)
			{
				this.ValidateSelectionStats();

				Vector2 curPos = new Vector2(cursorPos.X, cursorPos.Y);
				Vector2 lastPos = new Vector2(this.camActionBeginLoc.X, this.camActionBeginLoc.Y);
				this.camActionBeginLoc = new Point((int)curPos.X, (int)curPos.Y);

				float refZ = (this.SelectedObjects.Any() && camObj.Transform.Pos.Z < this.selectionCenter.Z - cam.NearZ) ? this.selectionCenter.Z : 0.0f;
				if (camObj.Transform.Pos.Z >= refZ - cam.NearZ)
					refZ = camObj.Transform.Pos.Z + MathF.Abs(cam.FocusDist);

				Vector2 targetOff = (-(curPos - lastPos) / this.GetScaleAtZ(refZ));
				Vector2 targetVel = targetOff / unscaledTimeMult;
				MathF.TransformCoord(ref targetVel.X, ref targetVel.Y, camObj.Transform.Angle);
				this.camVel.Z *= MathF.Pow(0.9f, unscaledTimeMult);
				this.camVel += (new Vector3(targetVel, this.camVel.Z) - this.camVel) * unscaledTimeMult;
				this.camTransformChanged = true;
			}
			else if (this.camAction == CameraAction.Move)
			{
				Vector3 moveVec = new Vector3(
					cursorPos.X - this.camActionBeginLoc.X,
					cursorPos.Y - this.camActionBeginLoc.Y,
					this.camVel.Z);

				const float BaseSpeedCursorLen = 25.0f;
				const float BaseSpeed = 3.0f;
				moveVec.X = BaseSpeed * MathF.Sign(moveVec.X) * MathF.Pow(MathF.Abs(moveVec.X) / BaseSpeedCursorLen, 1.5f);
				moveVec.Y = BaseSpeed * MathF.Sign(moveVec.Y) * MathF.Pow(MathF.Abs(moveVec.Y) / BaseSpeedCursorLen, 1.5f);

				MathF.TransformCoord(ref moveVec.X, ref moveVec.Y, camObj.Transform.Angle);

				if (this.camBeginDragScene)
				{
					float refZ = (this.SelectedObjects.Any() && camObj.Transform.Pos.Z < this.selectionCenter.Z - cam.NearZ) ? this.selectionCenter.Z : 0.0f;
					if (camObj.Transform.Pos.Z >= refZ - cam.NearZ)
						refZ = camObj.Transform.Pos.Z + MathF.Abs(cam.FocusDist);
					moveVec = new Vector3(moveVec.Xy * 0.5f / this.GetScaleAtZ(refZ), moveVec.Z);
				}

				this.camVel = moveVec;
				this.camTransformChanged = true;
			}
			else if (this.camVel.Length > 0.01f)
			{
				this.camVel *= MathF.Pow(0.9f, unscaledTimeMult);
				this.camTransformChanged = true;
			}
			else
			{
				this.camTransformChanged = this.camTransformChanged || (this.camVel != Vector3.Zero);
				this.camVel = Vector3.Zero;
			}
			
			if (this.camAction == CameraAction.RotateScene)
			{
				Vector2 center = new Vector2(this.ClientSize.Width, this.ClientSize.Height) * 0.5f;
				Vector2 curPos = new Vector2(cursorPos.X, cursorPos.Y);
				Vector2 lastPos = new Vector2(this.camActionBeginLoc.X, this.camActionBeginLoc.Y);
				this.camActionBeginLoc = new Point((int)curPos.X, (int)curPos.Y);

				float targetVel = (curPos - lastPos).X * MathF.RadAngle360 / 250.0f;
				targetVel *= (curPos.Y - center.Y) / center.Y;

				this.camAngleVel += (targetVel - this.camAngleVel) * unscaledTimeMult;
				this.camTransformChanged = true;
			}
			else if (this.camAction == CameraAction.Rotate)
			{
				float turnDir = 
					0.000125f * MathF.Sign(cursorPos.X - this.camActionBeginLoc.X) * 
					MathF.Pow(MathF.Abs(cursorPos.X - this.camActionBeginLoc.X), 1.25f);
				this.camAngleVel = turnDir;

				this.camTransformChanged = true;
			}
			else if (Math.Abs(this.camAngleVel) > 0.001f)
			{
				this.camAngleVel *= MathF.Pow(0.9f, unscaledTimeMult);
				this.camTransformChanged = true;
			}
			else
			{
				this.camTransformChanged = this.camTransformChanged || (this.camAngleVel != 0.0f);
				this.camAngleVel = 0.0f;
			}


			if (this.camTransformChanged)
			{
				camObj.Transform.MoveBy(this.camVel * unscaledTimeMult);
				camObj.Transform.TurnBy(this.camAngleVel * unscaledTimeMult);

				this.View.OnCamTransformChanged();
				this.Invalidate();
			}
			
			if (DualityApp.ExecContext == DualityApp.ExecutionContext.Game)
			{
				this.InvalidateSelectionStats();
				this.Invalidate();
			}
		}
		protected virtual void OnBeginAction(ObjectAction action) {}
		protected virtual void OnEndAction(ObjectAction action) {}

		protected virtual void OnSceneChanged()
		{
			if (this.mouseoverObject != null && this.mouseoverObject.IsInvalid) this.mouseoverObject = null;

			this.Invalidate();
		}
		protected virtual void OnCurrentCameraChanged(CamView.CameraChangedEventArgs e) {}
		protected virtual void OnGotFocus() {}
		protected virtual void OnLostFocus() {}

		protected virtual void OnDragEnter(DragEventArgs e) {}
		protected virtual void OnDragOver(DragEventArgs e) {}
		protected virtual void OnDragDrop(DragEventArgs e) {}
		protected virtual void OnDragLeave(EventArgs e) {}

		protected virtual void OnKeyDown(KeyEventArgs e) {}
		protected virtual void OnKeyUp(KeyEventArgs e) {}
		protected virtual void OnMouseDown(MouseEventArgs e) {}
		protected virtual void OnMouseUp(MouseEventArgs e) {}
		protected virtual void OnMouseMove(MouseEventArgs e) {}
		protected void OnMouseMove()
		{
			Point mousePos = this.PointToClient(Cursor.Position);
			this.OnMouseMove(new MouseEventArgs(Control.MouseButtons, 0, mousePos.X, mousePos.Y, 0));
		}


		public virtual SelObj PickSelObjAt(int x, int y)
		{
			return null;
		}
		public virtual List<SelObj> PickSelObjIn(int x, int y, int w, int h)
		{
			return new List<SelObj>();
		}
		public virtual void SelectObjects(IEnumerable<SelObj> selObjEnum, SelectMode mode = SelectMode.Set) {}
		public virtual void ClearSelection() {}
		protected virtual void PostPerformAction(IEnumerable<SelObj> selObjEnum, ObjectAction action) {}

		public virtual void DeleteObjects(IEnumerable<SelObj> objEnum) {}
		public virtual List<SelObj> CloneObjects(IEnumerable<SelObj> objEnum) { return new List<SelObj>(); }
		public void MoveSelectionBy(Vector3 move)
		{
			if (move == Vector3.Zero) return;

			UndoRedoManager.Do(new MoveCamViewObjAction(
				this.actionObjSel, 
				obj => this.PostPerformAction(obj, ObjectAction.Move), 
				move));

			this.drawSelGizmoState = ObjectAction.Move;
			this.InvalidateSelectionStats();
			this.Invalidate();
		}
		public void MoveSelectionTo(Vector3 target)
		{
			this.MoveSelectionBy(target - this.selectionCenter);
		}
		public void MoveSelectionToCursor()
		{
			Point mousePos = this.PointToClient(Cursor.Position);
			Vector3 mouseSpaceCoord = this.GetSpaceCoord(new Vector3(mousePos.X, mousePos.Y, this.selectionCenter.Z));

			// Apply user guide snapping
			if ((this.snapToUserGuides & UserGuideType.Position) != UserGuideType.None)
			{
				mouseSpaceCoord = this.EditingUserGuide.SnapPosition(mouseSpaceCoord);
			}

			this.MoveSelectionTo(mouseSpaceCoord);
		}
		public void RotateSelectionBy(float rotation)
		{
			if (rotation == 0.0f) return;
			
			UndoRedoManager.Do(new RotateCamViewObjAction(
				this.actionObjSel, 
				obj => this.PostPerformAction(obj, ObjectAction.Rotate), 
				rotation));

			this.drawSelGizmoState = ObjectAction.Rotate;
			this.InvalidateSelectionStats();
			this.Invalidate();
		}
		public void ScaleSelectionBy(float scale)
		{
			if (scale == 1.0f) return;

			UndoRedoManager.Do(new ScaleCamViewObjAction(
				this.actionObjSel, 
				obj => this.PostPerformAction(obj, ObjectAction.Scale), 
				scale));

			this.drawSelGizmoState = ObjectAction.Scale;
			this.InvalidateSelectionStats();
			this.Invalidate();
		}
		

		protected void SetDefaultActiveLayers(params Type[] activeLayers)
		{
			this.lastActiveLayers = activeLayers.ToList();
		}
		protected void SaveActiveLayers()
		{
			this.lastActiveLayers = this.View.ActiveLayers.Select(l => l.GetType()).ToList();
		}
		protected void RestoreActiveLayers()
		{
			this.View.SetActiveLayers(this.lastActiveLayers);
		}
		protected void SetDefaultObjectVisibility(params Type[] activeLayers)
		{
			this.lastObjVisibility = activeLayers.ToList();
		}
		protected void SaveObjectVisibility()
		{
			this.lastObjVisibility = this.View.ObjectVisibility.ToList();
		}
		protected void RestoreObjectVisibility()
		{
			this.View.SetObjectVisibility(this.lastObjVisibility);
		}

		protected void DrawSelectionMarkers(Canvas canvas, IEnumerable<SelObj> obj)
		{
			// Determine turned Camera axes for angle-independent drawing
			Vector2 catDotX, catDotY;
			float camAngle = this.CameraObj.Transform.Angle;
			MathF.GetTransformDotVec(camAngle, out catDotX, out catDotY);
			Vector3 right = new Vector3(1.0f, 0.0f, 0.0f);
			Vector3 down = new Vector3(0.0f, 1.0f, 0.0f);
			MathF.TransformDotVec(ref right, ref catDotX, ref catDotY);
			MathF.TransformDotVec(ref down, ref catDotX, ref catDotY);

			canvas.State.ZOffset = -1.0f;
			foreach (SelObj selObj in obj)
			{
				if (!selObj.HasTransform) continue;
				Vector3 posTemp = selObj.Pos;
				float scaleTemp = 1.0f;
				float radTemp = selObj.BoundRadius;

				if (!canvas.DrawDevice.IsCoordInView(posTemp, radTemp)) continue;

				// Draw selection marker
				if (selObj.ShowPos)
				{
					canvas.DrawDevice.PreprocessCoords(ref posTemp, ref scaleTemp);
					posTemp.Z = 0.0f;
					{
						VertexC1P3[] vertices = new VertexC1P3[4];
						vertices[0].Pos = posTemp - right * 10.0f;
						vertices[1].Pos = posTemp + right * 10.0f;
						vertices[2].Pos = posTemp - down * 10.0f;
						vertices[3].Pos = posTemp + down * 10.0f;
						canvas.DrawDevice.AddVertices(canvas.State.Material, VertexMode.Lines, vertices);
					}
				}

				// Draw angle marker
				if (selObj.ShowAngle)
				{
					posTemp = selObj.Pos + 
						radTemp * right * MathF.Sin(selObj.Angle - camAngle) - 
						radTemp * down * MathF.Cos(selObj.Angle - camAngle);
					canvas.DrawLine(selObj.Pos.X, selObj.Pos.Y, selObj.Pos.Z, posTemp.X, posTemp.Y, posTemp.Z);
				}

				// Draw boundary
				if (selObj.ShowBoundRadius && radTemp > 0.0f)
					canvas.DrawCircle(selObj.Pos.X, selObj.Pos.Y, selObj.Pos.Z, radTemp);
			}
			canvas.State.ZOffset = 0.0f;
		}
		protected void DrawLockedAxes(Canvas canvas, float x, float y, float z, float r)
		{
			Vector3 refPos = canvas.DrawDevice.RefCoord;
			float nearZ = canvas.DrawDevice.NearZ;

			canvas.PushState();
			if (this.actionLockedAxis == LockedAxis.X)
			{
				canvas.State.SetMaterial(new BatchInfo(DrawTechnique.Solid, ColorRgba.Lerp(this.FgColor, ColorRgba.Red, 0.5f)));
				canvas.DrawLine(x - r, y, z, x + r, y, z);
			}
			if (this.actionLockedAxis == LockedAxis.Y)
			{
				canvas.State.SetMaterial(new BatchInfo(DrawTechnique.Solid, ColorRgba.Lerp(this.FgColor, ColorRgba.Green, 0.5f)));
				canvas.DrawLine(x, y - r, z, x, y + r, z);
			}
			if (this.actionLockedAxis == LockedAxis.Z)
			{
				canvas.State.SetMaterial(new BatchInfo(DrawTechnique.Solid, ColorRgba.Lerp(this.FgColor, ColorRgba.Blue, 0.5f)));
				canvas.DrawLine(x, y, MathF.Max(z - r, refPos.Z + nearZ + 10), x, y, z);
				canvas.DrawLine(x, y, z, x, y, z + r);
			}
			canvas.PopState();
		}
		
		protected void BeginAction(ObjectAction action)
		{
			if (action == ObjectAction.None) return;
			Point mouseLoc = this.PointToClient(Cursor.Position);

			this.ValidateSelectionStats();

			this.camVel = Vector3.Zero;

			this.action = action;
			this.actionBeginLoc = mouseLoc;
			this.actionBeginLocSpace = this.GetSpaceCoord(new Vector3(
				mouseLoc.X, 
				mouseLoc.Y, 
				(this.action == ObjectAction.RectSelect) ? 0.0f : this.selectionCenter.Z));

			if (this.action == ObjectAction.Move)
				this.actionBeginLocSpace.Z = this.CameraObj.Transform.Pos.Z;

			this.actionLastLocSpace = this.actionBeginLocSpace;

			if (Sandbox.State == SandboxState.Playing)
				Sandbox.Freeze();

			this.OnBeginAction(this.action);
		}
		protected void EndAction()
		{
			if (this.action == ObjectAction.None) return;
			Point mouseLoc = this.PointToClient(Cursor.Position);

			if (this.action == ObjectAction.RectSelect)
			{
				this.activeRectSel = new ObjectSelection();
			}

			if (Sandbox.State == SandboxState.Playing)
				Sandbox.UnFreeze();

			this.OnEndAction(this.action);
			this.action = ObjectAction.None;

			if (this.actionIsClone)
			{
				this.actionIsClone = false;
				UndoRedoManager.EndMacro(UndoRedoManager.MacroDeriveName.FromFirst);
			}
			UndoRedoManager.Finish();
		}
		protected void UpdateAction()
		{
			Point mouseLoc = this.PointToClient(Cursor.Position);

			if (this.action == ObjectAction.RectSelect)
				this.UpdateRectSelection(mouseLoc);
			else if (this.action == ObjectAction.Move)
				this.UpdateObjMove(mouseLoc);
			else if (this.action == ObjectAction.Rotate)
				this.UpdateObjRotate(mouseLoc);
			else if (this.action == ObjectAction.Scale)
				this.UpdateObjScale(mouseLoc);
			else
				this.UpdateMouseover(mouseLoc);

			if (this.action != ObjectAction.None)
				this.InvalidateSelectionStats();
		}

		protected void InvalidateSelectionStats()
		{
			this.selectionStatsValid = false;
		}
		private void ValidateSelectionStats()
		{
			if (this.selectionStatsValid) return;
			
			List<SelObj> transformObjSel = this.allObjSel.Where(s => s.HasTransform).ToList();

			this.selectionCenter = Vector3.Zero;
			this.selectionRadius = 0.0f;

			foreach (SelObj s in transformObjSel)
				this.selectionCenter += s.Pos;
			if (transformObjSel.Count > 0) this.selectionCenter /= transformObjSel.Count;

			foreach (SelObj s in transformObjSel)
				this.selectionRadius = MathF.Max(this.selectionRadius, s.BoundRadius + (s.Pos - this.selectionCenter).Length);

			this.selectionStatsValid = true;
		}

		protected void UpdateMouseover(Point mouseLoc)
		{
			bool lastMouseoverSelect = this.mouseoverSelect;
			SelObj lastMouseoverObject = this.mouseoverObject;
			ObjectAction lastMouseoverAction = this.mouseoverAction;

			if (this.actionAllowed && !this.camBeginDragScene && this.camAction == CameraAction.None)
			{
				this.ValidateSelectionStats();

				// Determine object at mouse position
				this.mouseoverObject = this.PickSelObjAt(mouseLoc.X, mouseLoc.Y);

				// Determine action variables
				Vector3 mouseSpaceCoord = this.GetSpaceCoord(new Vector3(mouseLoc.X, mouseLoc.Y, this.selectionCenter.Z));
				float scale = this.GetScaleAtZ(this.selectionCenter.Z);
				const float boundaryThickness = 10.0f;
				bool tooSmall = this.selectionRadius * scale <= boundaryThickness * 2.0f;
				bool mouseOverBoundary = MathF.Abs((mouseSpaceCoord - this.selectionCenter).Length - this.selectionRadius) * scale < boundaryThickness;
				bool mouseInsideBoundary = !mouseOverBoundary && (mouseSpaceCoord - this.selectionCenter).Length < this.selectionRadius;
				bool mouseAtCenterAxis = 
					MathF.Abs(mouseSpaceCoord.X - this.selectionCenter.X) * scale < boundaryThickness || 
					MathF.Abs(mouseSpaceCoord.Y - this.selectionCenter.Y) * scale < boundaryThickness;
				bool shift = (Control.ModifierKeys & Keys.Shift) != Keys.None;
				bool ctrl = (Control.ModifierKeys & Keys.Control) != Keys.None;

				bool anySelection = this.actionObjSel.Count > 0;
				bool canMove = this.actionObjSel.Any(s => s.IsActionAvailable(ObjectAction.Move));
				bool canRotate = (canMove && this.actionObjSel.Count > 1) || this.actionObjSel.Any(s => s.IsActionAvailable(ObjectAction.Rotate));
				bool canScale = (canMove && this.actionObjSel.Count > 1) || this.actionObjSel.Any(s => s.IsActionAvailable(ObjectAction.Scale));

				// Select which action to propose
				this.mouseoverSelect = false;
				if (ctrl)
					this.mouseoverAction = ObjectAction.RectSelect;
				else if (anySelection && !tooSmall && mouseOverBoundary && mouseAtCenterAxis && this.selectionRadius > 0.0f && canScale)
					this.mouseoverAction = ObjectAction.Scale;
				else if (anySelection && !tooSmall && mouseOverBoundary && canRotate)
					this.mouseoverAction = ObjectAction.Rotate;
				else if (anySelection && mouseInsideBoundary && canMove)
					this.mouseoverAction = ObjectAction.Move;
				else if (shift) // Lower prio than Ctrl, because Shift also modifies mouse actions
					this.mouseoverAction = ObjectAction.RectSelect;
				else if (this.mouseoverObject != null && this.mouseoverObject.IsActionAvailable(ObjectAction.Move))
				{
					this.mouseoverAction = ObjectAction.Move; 
					this.mouseoverSelect = true;
				}
				else
					this.mouseoverAction = ObjectAction.RectSelect;
			}
			else
			{
				this.mouseoverObject = null;
				this.mouseoverSelect = false;
				this.mouseoverAction = ObjectAction.None;
			}

			// If mouseover changed..
			if (this.mouseoverObject != lastMouseoverObject || 
				this.mouseoverSelect != lastMouseoverSelect ||
				this.mouseoverAction != lastMouseoverAction)
			{
				// Adjust mouse cursor based on proposed action
				if (this.mouseoverAction == ObjectAction.Move)
					this.Cursor = CursorHelper.ArrowActionMove;
				else if (this.mouseoverAction == ObjectAction.Rotate)
					this.Cursor = CursorHelper.ArrowActionRotate;
				else if (this.mouseoverAction == ObjectAction.Scale)
					this.Cursor = CursorHelper.ArrowActionScale;
				else
					this.Cursor = CursorHelper.Arrow;
			}
			
			// Redraw if action gizmos might be visible
			if (this.actionAllowed)
				this.Invalidate();
		}
		private void UpdateRectSelection(Point mouseLoc)
		{
			if (DualityEditorApp.IsSelectionChanging) return; // Prevent Recursion in case SelectObjects triggers UpdateAction.

			bool shift = (Control.ModifierKeys & Keys.Shift) != Keys.None;
			bool ctrl = (Control.ModifierKeys & Keys.Control) != Keys.None;

			// Determine picked rect
			int pX = Math.Max(Math.Min(mouseLoc.X, this.actionBeginLoc.X), 0);
			int pY = Math.Max(Math.Min(mouseLoc.Y, this.actionBeginLoc.Y), 0);
			int pX2 = Math.Max(mouseLoc.X, this.actionBeginLoc.X);
			int pY2 = Math.Max(mouseLoc.Y, this.actionBeginLoc.Y);
			int pW = Math.Max(pX2 - pX, 1);
			int pH = Math.Max(pY2 - pY, 1);

			// Check which renderers are picked
			List<SelObj> picked = this.PickSelObjIn(pX, pY, pW, pH);

			// Store in internal rect selection
			ObjectSelection oldRectSel = this.activeRectSel;
			this.activeRectSel = new ObjectSelection(picked);

			// Apply internal selection to actual editor selection
			if (shift || ctrl)
			{
				if (this.activeRectSel.ObjectCount > 0)
				{
					ObjectSelection added = (this.activeRectSel - oldRectSel) + (oldRectSel - this.activeRectSel);
					this.SelectObjects(added.OfType<SelObj>(), shift ? SelectMode.Append : SelectMode.Toggle);
				}
			}
			else if (this.activeRectSel.ObjectCount > 0)
				this.SelectObjects(this.activeRectSel.OfType<SelObj>());
			else
				this.ClearSelection();

			this.Invalidate();
		}
		private void UpdateObjMove(Point mouseLoc)
		{
			this.ValidateSelectionStats();

			// Determine where to move the object
			float zMovement = this.CameraObj.Transform.Pos.Z - this.actionLastLocSpace.Z;
			Vector3 mousePosSpace = this.GetSpaceCoord(new Vector3(mouseLoc.X, mouseLoc.Y, this.selectionCenter.Z + zMovement)); mousePosSpace.Z = 0;
			Vector3 resetMovement = this.actionBeginLocSpace - this.actionLastLocSpace;
			Vector3 targetMovement = mousePosSpace - this.actionLastLocSpace; targetMovement.Z = zMovement;

			// Apply user guide snapping
			if ((this.snapToUserGuides & UserGuideType.Position) != UserGuideType.None)
			{
				Vector3 snappedCenter = this.selectionCenter;
				Vector3 targetPosSpace = snappedCenter + targetMovement;

				// When moving multiple objects, snap only relative to the original selection center, so individual grid alignment is retained
				if (this.actionObjSel.Count > 1)
					snappedCenter = this.EditingUserGuide.SnapPosition(this.selectionCenter);

				targetPosSpace = this.EditingUserGuide.SnapPosition(targetPosSpace);
				targetMovement = targetPosSpace - snappedCenter;
			}

			// Apply user axis locks
			targetMovement = this.ApplyAxisLock(targetMovement, resetMovement, mousePosSpace - this.actionBeginLocSpace + new Vector3(0.0f, 0.0f, this.CameraObj.Transform.Pos.Z));

			// Move the selected objects accordingly
			this.MoveSelectionBy(targetMovement);

			this.actionLastLocSpace += targetMovement;
		}
		private void UpdateObjRotate(Point mouseLoc)
		{
			this.ValidateSelectionStats();

			Vector3 spaceCoord = this.GetSpaceCoord(new Vector3(mouseLoc.X, mouseLoc.Y, this.selectionCenter.Z));
			float lastAngle = MathF.Angle(this.selectionCenter.X, this.selectionCenter.Y, this.actionLastLocSpace.X, this.actionLastLocSpace.Y);
			float curAngle = MathF.Angle(this.selectionCenter.X, this.selectionCenter.Y, spaceCoord.X, spaceCoord.Y);
			float rotation = curAngle - lastAngle;

			this.RotateSelectionBy(rotation);

			this.actionLastLocSpace = spaceCoord;
		}
		private void UpdateObjScale(Point mouseLoc)
		{
			this.ValidateSelectionStats();
			if (this.selectionRadius == 0.0f) return;

			Vector3 spaceCoord = this.GetSpaceCoord(new Vector3(mouseLoc.X, mouseLoc.Y, this.selectionCenter.Z));
			float lastRadius = this.selectionRadius;
			float curRadius = (this.selectionCenter - spaceCoord).Length;

			if ((this.snapToUserGuides & UserGuideType.Scale) != UserGuideType.None)
			{
				curRadius = this.EditingUserGuide.SnapSize(curRadius);
			}

			float scale = MathF.Clamp(curRadius / lastRadius, 0.0001f, 10000.0f);
			this.ScaleSelectionBy(scale);

			this.actionLastLocSpace = spaceCoord;
			this.Invalidate();
		}

		/// <summary>
		/// Returns an axis-locked version of the specified vector, if requested by the user. Doesn't
		/// do anything when no axis lock is in currently active.
		/// </summary>
		/// <param name="baseVec">The base vector without any locking in place.</param>
		/// <param name="lockedVec">A reference vector that represents the base vector being locked to all axes at once.</param>
		/// <param name="beginToTarget">The movement vector to evaluate in order to determine the axes to which the base vector will be locked.</param>
		/// <returns></returns>
		protected Vector3 ApplyAxisLock(Vector3 baseVec, Vector3 lockedVec, Vector3 beginToTarget)
		{
			bool shift = (Control.ModifierKeys & Keys.Shift) != Keys.None;
			if (!shift)
			{
				this.actionLockedAxis = LockedAxis.None;
				return baseVec;
			}
			else
			{
				float xWeight = MathF.Abs(Vector3.Dot(beginToTarget.Normalized, Vector3.UnitX));
				float yWeight = MathF.Abs(Vector3.Dot(beginToTarget.Normalized, Vector3.UnitY));
				float zWeight = MathF.Abs(Vector3.Dot(beginToTarget.Normalized, Vector3.UnitZ));
				
				if (xWeight >= yWeight && xWeight >= zWeight)
				{
					this.actionLockedAxis = LockedAxis.X;
					return new Vector3(baseVec.X, lockedVec.Y, lockedVec.Z);
				}
				else if (yWeight >= xWeight && yWeight >= zWeight)
				{
					this.actionLockedAxis = LockedAxis.Y;
					return new Vector3(lockedVec.X, baseVec.Y, lockedVec.Z);
				}
				else if (zWeight >= yWeight && zWeight >= xWeight)
				{
					this.actionLockedAxis = LockedAxis.Z;
					return new Vector3(lockedVec.X, lockedVec.Y, baseVec.Z);
				}
				return lockedVec;
			}
		}
		protected Vector2 ApplyAxisLock(Vector2 baseVec, Vector2 lockedVec, Vector2 beginToTarget)
		{
			return this.ApplyAxisLock(new Vector3(baseVec), new Vector3(lockedVec), new Vector3(beginToTarget)).Xy;
		}
		protected Vector3 ApplyAxisLock(Vector3 targetVec, Vector3 lockedVec)
		{
			return targetVec + this.ApplyAxisLock(Vector3.Zero, lockedVec - targetVec, lockedVec - targetVec);
		}
		protected Vector2 ApplyAxisLock(Vector2 targetVec, Vector2 lockedVec)
		{
			return targetVec + this.ApplyAxisLock(Vector2.Zero, lockedVec - targetVec, lockedVec - targetVec);
		}
		
		protected void CollectLayerDrawcalls(Canvas canvas)
		{
			var layers = this.View.ActiveLayers.ToArray();
			layers.StableSort((a, b) => a.Priority - b.Priority);
			foreach (var layer in layers)
			{
				canvas.PushState();
				layer.OnCollectDrawcalls(canvas);
				canvas.PopState();
			}
		}
		protected void CollectLayerOverlayDrawcalls(Canvas canvas)
		{
			var layers = this.View.ActiveLayers.ToArray();
			layers.StableSort((a, b) => a.Priority - b.Priority);
			foreach (var layer in layers)
			{
				canvas.PushState();
				layer.OnCollectOverlayDrawcalls(canvas);
				canvas.PopState();
			}
		}
		protected void CollectLayerBackgroundDrawcalls(Canvas canvas)
		{
			var layers = this.View.ActiveLayers.ToArray();
			layers.StableSort((a, b) => a.Priority - b.Priority);
			foreach (var layer in layers)
			{
				canvas.PushState();
				layer.OnCollectBackgroundDrawcalls(canvas);
				canvas.PopState();
			}
		}

		private void UpdateFormattedTextRenderers()
		{
			this.statusText.MaxWidth = this.ClientSize.Width - 20;
			this.statusText.MaxHeight = this.ClientSize.Height - 20;
			this.statusText.Fonts = new [] { OverlayFont };
			this.actionText.MaxWidth = MathF.Min(500, this.ClientSize.Width / 2);
			this.actionText.MaxHeight = MathF.Min(500, this.ClientSize.Height / 2);
			this.actionText.Fonts = new [] { OverlayFont };
		}

		private void RenderableControl_Paint(object sender, PaintEventArgs e)
		{
			if (DualityApp.ExecContext == DualityApp.ExecutionContext.Terminated) return;

			// Retrieve OpenGL context
 			try { this.RenderableSite.MakeCurrent(); } catch (Exception) { return; }

			try
			{
				this.CameraComponent.Passes.Add(this.camPassBg);
				this.CameraComponent.Passes.Add(this.camPassEdWorld);
				this.CameraComponent.Passes.Add(this.camPassEdScreen);

				this.OnRenderState();

				this.CameraComponent.Passes.Remove(this.camPassBg);
				this.CameraComponent.Passes.Remove(this.camPassEdWorld);
				this.CameraComponent.Passes.Remove(this.camPassEdScreen);
			}
			catch (Exception exception)
			{
				Log.Editor.WriteError("An error occurred during CamView {1} rendering. The current DrawDevice state may be compromised. Exception: {0}", Log.Exception(exception), this.CameraComponent.ToString());
			}
			
			this.RenderableSite.SwapBuffers();
		}
		private void RenderableControl_MouseMove(object sender, MouseEventArgs e)
		{
			this.mouseover = true;
			this.UpdateAction();
			if (!this.camBeginDragScene) this.OnMouseMove(e);
		}
		private void RenderableControl_MouseUp(object sender, MouseEventArgs e)
		{
			this.drawCamGizmoState = CameraAction.None;
			this.drawSelGizmoState = ObjectAction.None;

			if (this.camBeginDragScene)
			{
				this.camAction = CameraAction.None;
				this.Cursor = CursorHelper.HandGrab;
			}
			else
			{
				if (this.action == ObjectAction.RectSelect && this.actionBeginLoc == e.Location)
					this.UpdateRectSelection(e.Location);

				if (e.Button == MouseButtons.Left)
					this.EndAction();

				if (this.camAction == CameraAction.Move && e.Button == MouseButtons.Middle)
					this.camAction = CameraAction.None;
				else if (this.camAction == CameraAction.Rotate && e.Button == MouseButtons.Right)
					this.camAction = CameraAction.None;

				this.OnMouseUp(e);
			}

			this.Invalidate();
		}
		private void RenderableControl_MouseDown(object sender, MouseEventArgs e)
		{
			bool alt = (Control.ModifierKeys & Keys.Alt) != Keys.None;

			this.drawCamGizmoState = CameraAction.None;
			this.drawSelGizmoState = ObjectAction.None;

			if (this.camBeginDragScene)
			{
				this.camActionBeginLoc = e.Location;
				if (e.Button == MouseButtons.Left)
				{
					this.camAction = CameraAction.DragScene;
					this.camActionBeginLocSpace = this.CameraObj.Transform.RelativePos;
					this.Cursor = CursorHelper.HandGrabbing;
				}
				else if (e.Button == MouseButtons.Right)
				{
					this.camAction = CameraAction.RotateScene;
					this.camActionBeginLocSpace = this.CameraObj.Transform.RelativePos;
					this.Cursor = CursorHelper.HandGrabbing;
				}
				else if (e.Button == MouseButtons.Middle)
				{
					this.camAction = CameraAction.Move;
					this.camActionBeginLocSpace = this.CameraObj.Transform.RelativePos;
				}
			}
			else
			{
				if (this.action == ObjectAction.None)
				{
					if (e.Button == MouseButtons.Left)
					{
						if (this.mouseoverSelect)
						{
							// To interact with an object that isn't selected yet: Select it.
							if (!this.allObjSel.Contains(this.mouseoverObject))
								this.SelectObjects(new [] { this.mouseoverObject });
						}
						if (alt)
						{
							UndoRedoManager.BeginMacro();
							this.actionIsClone = true;
							this.SelectObjects(this.CloneObjects(this.actionObjSel));
						}
						this.BeginAction(this.mouseoverAction);
					}
				}

				if (this.camActionAllowed && this.camAction == CameraAction.None)
				{
					this.camActionBeginLoc = e.Location;
					if (e.Button == MouseButtons.Middle)
					{
						this.camAction = CameraAction.Move;
						this.camActionBeginLocSpace = this.CameraObj.Transform.RelativePos;
					}
					else if (e.Button == MouseButtons.Right)
					{
						this.camAction = CameraAction.Rotate;
						this.camActionBeginLocSpace = new Vector3(this.CameraObj.Transform.RelativeAngle, 0.0f, 0.0f);
					}
				}

				this.OnMouseDown(e);
			}
		}
		private void RenderableControl_MouseWheel(object sender, MouseEventArgs e)
		{
			if (!this.mouseover) return;

			this.drawCamGizmoState = CameraAction.None;
			this.drawSelGizmoState = ObjectAction.None;

			if (e.Delta != 0)
			{
				if (this.View.PerspectiveMode == PerspectiveMode.Parallax)
				{
					GameObject camObj = this.CameraObj;
					float curVel = this.camVel.Length * MathF.Sign(this.camVel.Z);
					Vector2 curTemp = new Vector2(
						(e.X * 2.0f / this.ClientSize.Width) - 1.0f,
						(e.Y * 2.0f / this.ClientSize.Height) - 1.0f);
					MathF.TransformCoord(ref curTemp.X, ref curTemp.Y, camObj.Transform.RelativeAngle);

					if (MathF.Sign(e.Delta) != MathF.Sign(curVel))
						curVel = 0.0f;
					else
						curVel *= 1.5f;
					curVel += 0.015f * e.Delta;
					curVel = MathF.Sign(curVel) * MathF.Min(MathF.Abs(curVel), 500.0f);

					Vector3 movVec = new Vector3(
						MathF.Sign(e.Delta) * MathF.Sign(curTemp.X) * MathF.Pow(curTemp.X, 2.0f), 
						MathF.Sign(e.Delta) * MathF.Sign(curTemp.Y) * MathF.Pow(curTemp.Y, 2.0f), 
						1.0f);
					movVec.Normalize();
					this.camVel = movVec * curVel;
				}
				else
				{
					this.View.FocusDist = this.View.FocusDist + this.View.FocusDistIncrement * e.Delta / 40;
				}
			}
		}
		private void RenderableControl_MouseLeave(object sender, EventArgs e)
		{
			this.UpdateAction();
			if (!this.camBeginDragScene) this.OnMouseMove();

			this.mouseoverAction = ObjectAction.None;
			this.mouseoverObject = null;
			this.mouseoverSelect = false;
			this.mouseover = false;

			this.Invalidate();
		}
		private void RenderableControl_KeyDown(object sender, KeyEventArgs e)
		{
			if (this.actionAllowed)
			{
				if (e.KeyCode == Keys.Delete)
				{
					List<SelObj> deleteList = this.actionObjSel.ToList();
					this.ClearSelection();
					this.DeleteObjects(deleteList);
				}
				else if (e.KeyCode == Keys.C && e.Control)
				{
					List<SelObj> cloneList = this.CloneObjects(this.actionObjSel);
					this.SelectObjects(cloneList);
				}
				else if (e.KeyCode == Keys.G)
				{
					if (e.Alt)
					{
						this.SelectObjects(this.CloneObjects(this.actionObjSel));
						e.SuppressKeyPress = true; // Prevent menustrip from getting focused
					}
					this.MoveSelectionToCursor();
				}
				else if (!e.Control && e.KeyCode == Keys.Left)		this.MoveSelectionBy(-Vector3.UnitX);
				else if (!e.Control && e.KeyCode == Keys.Right)		this.MoveSelectionBy(Vector3.UnitX);
				else if (!e.Control && e.KeyCode == Keys.Up)		this.MoveSelectionBy(-Vector3.UnitY);
				else if (!e.Control && e.KeyCode == Keys.Down)		this.MoveSelectionBy(Vector3.UnitY);
				else if (!e.Control && e.KeyCode == Keys.Add)		this.MoveSelectionBy(Vector3.UnitZ);
				else if (!e.Control && e.KeyCode == Keys.Subtract)	this.MoveSelectionBy(-Vector3.UnitZ);
				else if (e.KeyCode == Keys.ShiftKey)				this.UpdateAction();
				else if (e.KeyCode == Keys.ControlKey)				this.UpdateAction();
			}

			if (this.camActionAllowed)
			{
				if (e.KeyCode == Keys.Space && this.action == ObjectAction.None && !this.camBeginDragScene)
				{
					this.camBeginDragScene = true;
					this.UpdateAction();
					this.Cursor = CursorHelper.HandGrab;
				}
				else if (e.KeyCode == Keys.F)
				{
					if (DualityEditorApp.Selection.MainGameObject != null)
						this.View.FocusOnObject(DualityEditorApp.Selection.MainGameObject);
					else
						this.View.ResetCamera();
				}
				else if (e.Control && e.KeyCode == Keys.Left)
				{
					this.drawCamGizmoState = CameraAction.Move;
					Vector3 pos = this.CameraObj.Transform.Pos;
					pos.X = MathF.Round(pos.X - 1.0f);
					this.CameraObj.Transform.Pos = pos;
					this.Invalidate();
				}
				else if (e.Control && e.KeyCode == Keys.Right)
				{
					this.drawCamGizmoState = CameraAction.Move;
					Vector3 pos = this.CameraObj.Transform.Pos;
					pos.X = MathF.Round(pos.X + 1.0f);
					this.CameraObj.Transform.Pos = pos;
					this.Invalidate();
				}
				else if (e.Control && e.KeyCode == Keys.Up)
				{
					this.drawCamGizmoState = CameraAction.Move;
					Vector3 pos = this.CameraObj.Transform.Pos;
					pos.Y = MathF.Round(pos.Y - 1.0f);
					this.CameraObj.Transform.Pos = pos;
					this.Invalidate();
				}
				else if (e.Control && e.KeyCode == Keys.Down)
				{
					this.drawCamGizmoState = CameraAction.Move;
					Vector3 pos = this.CameraObj.Transform.Pos;
					pos.Y = MathF.Round(pos.Y + 1.0f);
					this.CameraObj.Transform.Pos = pos;
					this.Invalidate();
				}
				else if (e.Control && e.KeyCode == Keys.Add)
				{
					this.drawCamGizmoState = CameraAction.Move;
					Vector3 pos = this.CameraObj.Transform.Pos;
					pos.Z = MathF.Round(pos.Z + 1.0f);
					this.CameraObj.Transform.Pos = pos;
					this.Invalidate();
				}
				else if (e.Control && e.KeyCode == Keys.Subtract)
				{
					this.drawCamGizmoState = CameraAction.Move;
					Vector3 pos = this.CameraObj.Transform.Pos;
					pos.Z = MathF.Round(pos.Z - 1.0f);
					this.CameraObj.Transform.Pos = pos;
					this.Invalidate();
				}
			}

			this.OnKeyDown(e);
		}
		private void RenderableControl_KeyUp(object sender, KeyEventArgs e)
		{
			if (e.KeyCode == Keys.Space && this.camBeginDragScene)
			{
				this.camBeginDragScene = false;
				this.camAction = CameraAction.None;
				this.Cursor = CursorHelper.Arrow;
				this.UpdateAction();
			}
			else if (e.KeyCode == Keys.ShiftKey)
			{
				this.actionLockedAxis = LockedAxis.None;
				this.UpdateAction();
			}
			else if (e.KeyCode == Keys.ControlKey)
				this.UpdateAction();

			this.OnKeyUp(e);
		}
		private void RenderableControl_GotFocus(object sender, EventArgs e)
		{
			this.MakeDualityTarget();
			this.OnGotFocus();
		}
		private void RenderableControl_LostFocus(object sender, EventArgs e)
		{
			if (DualityEditorApp.MainForm == null) return;

			this.camAction = CameraAction.None;
			this.EndAction();
			this.OnLostFocus();
			this.Invalidate();
		}
		private void RenderableControl_DragOver(object sender, DragEventArgs e)
		{
			this.OnDragOver(e);
			// Force immediate buffer swap, because there is no event loop while dragging.
			DualityEditorApp.PerformBufferSwap();
		}
		private void RenderableControl_DragLeave(object sender, EventArgs e)
		{
			this.OnDragLeave(e);
		}
		private void RenderableControl_DragEnter(object sender, DragEventArgs e)
		{
			this.OnDragEnter(e);
		}
		private void RenderableControl_DragDrop(object sender, DragEventArgs e)
		{
			this.OnDragDrop(e);
		}
		private void RenderableControl_Resize(object sender, EventArgs e)
		{
			this.UpdateFormattedTextRenderers();
		}
		private void View_FocusDistChanged(object sender, EventArgs e)
		{
			this.UpdateAction();
			if (!this.camBeginDragScene) this.OnMouseMove();
		}
		private void View_CurrentCameraChanged(object sender, CamView.CameraChangedEventArgs e)
		{
			this.OnCurrentCameraChanged(e);
		}
		private void DualityEditorApp_UpdatingEngine(object sender, EventArgs e)
		{
			this.OnUpdateState();
		}
		private void DualityEditorApp_ObjectPropertyChanged(object sender, ObjectPropertyChangedEventArgs e)
		{
			if (e.HasAnyProperty(ReflectionInfo.Property_Transform_RelativePos, ReflectionInfo.Property_Transform_RelativeAngle) &&
				e.Objects.Components.Any(c => c.GameObj == this.CameraObj))
			{
				this.UpdateAction();
				if (!this.camBeginDragScene) this.OnMouseMove();
			}
		}
		private void Scene_Changed(object sender, EventArgs e)
		{
			this.OnSceneChanged();
		}
		private void camPassEdScreen_CollectDrawcalls(object sender, CollectDrawcallEventArgs e)
		{
			Canvas canvas = new Canvas(e.Device);
			canvas.State.SetMaterial(new BatchInfo(DrawTechnique.Mask, this.FgColor));
			canvas.State.TextFont = OverlayFont;

			this.OnCollectStateOverlayDrawcalls(canvas);
		}
		private void camPassEdWorld_CollectDrawcalls(object sender, CollectDrawcallEventArgs e)
		{
			Canvas canvas = new Canvas(e.Device);
			canvas.State.SetMaterial(new BatchInfo(DrawTechnique.Mask, this.FgColor));
			canvas.State.TextFont = Duality.Resources.Font.GenericMonospace8;

			this.OnCollectStateDrawcalls(canvas);
		}
		private void camPassBg_CollectDrawcalls(object sender, CollectDrawcallEventArgs e)
		{
			Canvas canvas = new Canvas(e.Device);
			canvas.State.SetMaterial(new BatchInfo(DrawTechnique.Mask, this.FgColor));
			canvas.State.TextFont = Duality.Resources.Font.GenericMonospace8;

			this.OnCollectStateBackgroundDrawcalls(canvas);
		}

		public virtual HelpInfo ProvideHoverHelp(Point localPos, ref bool captured)
		{
			if (this.actionAllowed && this.SelectedObjects.Any())
			{
				return HelpInfo.FromText(CamViewRes.CamView_Help_ObjActions, 
					CamViewRes.CamView_Help_ObjActions_Delete + "\n" +
					CamViewRes.CamView_Help_ObjActions_Clone + "\n" +
					CamViewRes.CamView_Help_ObjActions_EditClone + "\n" +
					CamViewRes.CamView_Help_ObjActions_MoveStep + "\n" +
					CamViewRes.CamView_Help_ObjActions_Focus + "\n" +
					CamViewRes.CamView_Help_ObjActions_AxisLock);
			}
			else if (this.camActionAllowed)
			{
				return HelpInfo.FromText(CamViewRes.CamView_Help_CamActions, 
					CamViewRes.CamView_Help_CamActions_Move + "\n" +
					CamViewRes.CamView_Help_CamActions_MoveAlternate + "\n" +
					CamViewRes.CamView_Help_CamActions_MoveStep + "\n" +
					CamViewRes.CamView_Help_CamActions_Focus);
			}

			return null;
		}
	}
}
