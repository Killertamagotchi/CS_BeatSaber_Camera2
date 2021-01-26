﻿using System;
using System.Linq;
using System.Text;
using UnityEngine;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Camera2.Behaviours;
using Camera2.Utils;

namespace Camera2.Configuration {
	enum CameraType {
		FirstPerson,
		Attached, //Unused for now, but mostly implemented - For parenting to arbitrary things
		Positionable
	}
	enum WallVisiblity {
		Visible,
		Transparent,
		Hidden
	}

	[JsonObject(MemberSerialization.OptIn)]
	class GameObjects {
		private CameraSettings parentSetting;
		public GameObjects(CameraSettings parentSetting) {
			this.parentSetting = parentSetting;
		}

		[JsonConverter(typeof(StringEnumConverter)), JsonProperty("Walls")]
		private WallVisiblity _Walls = WallVisiblity.Visible;
		[JsonProperty("Debris")]
		private bool _Debris = true; //Maybe make Enum w/ Show / Hide / Linked like in Cam Plus
		[JsonProperty("UI")]
		private bool _UI = true;
		[JsonProperty("Avatar")]
		private bool _Avatar = true;
		[JsonProperty("Floor")]
		private bool _Floor = true;
		[JsonProperty("Notes")]
		private bool _Notes = true;
		//[JsonProperty("EverythingElse")]
		//private bool _EverythingElse = true;


		public WallVisiblity Walls { get { return _Walls; } set { _Walls = value; parentSetting.ApplyLayerBitmask(); } }
		public bool Debris { get { return _Debris; } set { _Debris = value; parentSetting.ApplyLayerBitmask(); } }
		public bool UI { get { return _UI; } set { _UI = value; parentSetting.ApplyLayerBitmask(); } }
		public bool Avatar { get { return _Avatar; } set { _Avatar = value; parentSetting.ApplyLayerBitmask(); } }
		public bool Floor { get { return _Floor; } set { _Floor = value; parentSetting.ApplyLayerBitmask(); } }
		public bool Notes { get { return _Notes; } set { _Notes = value; parentSetting.ApplyLayerBitmask(); } }
		//public bool EverythingElse { get { return _EverythingElse; } set { _EverythingElse = value; parentSetting.ApplyLayerBitmask(); } }
	}
	
	class CameraSettings {
		[JsonIgnore]
		private Cam2 cam;
		public CameraSettings(Cam2 cam) {
			this.cam = cam;

			visibleObjects = new GameObjects(this);
		}

		public void Load(bool loadConfig = true) {
			// Set default values incase they're removed from the JSON because of user stoopid
			FOV = 90;
			viewRect = new Rect(0, 0, Screen.width, Screen.height);

			if(loadConfig && System.IO.File.Exists(cam.configPath)) {
				JsonConvert.PopulateObject(System.IO.File.ReadAllText(cam.configPath, Encoding.ASCII), this, new JsonSerializerSettings {
					NullValueHandling = NullValueHandling.Ignore,
					Error = (se, ev) => { ev.ErrorContext.Handled = true; }
				});
			} else {
				layer = CamManager.cams.Count == 0 ? -1000 : CamManager.cams.Max(x => x.Value.settings.layer) - 1;

				Save();
			}

			ApplyPositionAndRotation();
			ApplyLayerBitmask();
			cam.ActivateWorldCamIfNecessary();
			// Trigger setter for cam aspect ratio
			viewRect = viewRect;
			cam.UpdateRenderTexture();
		}

		public void ApplyPositionAndRotation() {
			if(type != CameraType.Positionable)
				return;

			cam.transform.position = targetPos;
			cam.transform.eulerAngles = targetRot;
		}

		public void ApplyLayerBitmask() {
			//var maskBuilder = visibleObjects.EverythingElse ? CamManager.baseCullingMask : 0;
			var maskBuilder = CamManager.baseCullingMask;

			foreach(int mask in Enum.GetValues(typeof(VisibilityMasks)))
				maskBuilder &= ~mask;

			if(visibleObjects.Walls == WallVisiblity.Visible || (ModmapExtensions.autoOpaqueWalls && SceneUtil.isProbablyInWallMap)) {
				maskBuilder |= (int)VisibilityMasks.Walls | (int)VisibilityMasks.WallTextures;
			} else if(visibleObjects.Walls == WallVisiblity.Transparent) {
				maskBuilder |= (int)VisibilityMasks.Walls;
			}

			if(visibleObjects.Floor) maskBuilder |= (int)VisibilityMasks.Floor;
			if(visibleObjects.Notes) maskBuilder |= (int)VisibilityMasks.Notes;
			if(visibleObjects.Debris) maskBuilder |= (int)VisibilityMasks.Debris;
			if(visibleObjects.UI) maskBuilder |= (int)VisibilityMasks.UI;
			if(visibleObjects.Avatar) maskBuilder |= (int)VisibilityMasks.Avatar;

			maskBuilder |= (int)(type == CameraType.FirstPerson ? VisibilityMasks.FirstPerson : VisibilityMasks.ThirdPerson);

			if(cam.UCamera.cullingMask != maskBuilder)
				cam.UCamera.cullingMask = maskBuilder;
		}

		public void Save() {
			System.IO.File.WriteAllText(cam.configPath, JsonConvert.SerializeObject(this, Formatting.Indented), Encoding.ASCII);
		}

		[JsonConverter(typeof(StringEnumConverter)), JsonProperty("type")]
		private CameraType _type = CameraType.Attached;
		[JsonIgnore]
		public CameraType type {
			get { return _type; }
			set {
				_type = value;
				cam.ActivateWorldCamIfNecessary();
				ApplyLayerBitmask();
			}
		}

		[JsonProperty("showWorldCam")]
		private bool _showWorldCam = true;
		[JsonIgnore]
		public bool showWorldCam {
			get { return _showWorldCam; }
			set {
				_showWorldCam = value;
				cam.ActivateWorldCamIfNecessary();
			}
		}

		public float FOV { get { return cam.UCamera.fieldOfView; } set { cam.UCamera.fieldOfView = value; } }
		public int layer {
			get { return (int)cam.UCamera.depth; }
			set {
				cam.UCamera.depth = value;
				CamManager.ApplyViewportLayers();
			}
		}

		private int _antiAliasing = 1;
		public int antiAliasing {
			get { return _antiAliasing; }
			set {
				_antiAliasing = Math.Min(Math.Max(value, 0), 8);
				cam.UpdateRenderTexture();
			}
		}
		

		public GameObjects visibleObjects { get; private set; }

		private float _renderScale = 1;
		public float renderScale {
			get { return _renderScale; }
			set {
				if(_renderScale == value) return;
				_renderScale = Math.Min(value, 3);
				cam.UpdateRenderTexture();
			}
		}
		
		private Rect _viewRect = Rect.zero;
		[JsonConverter(typeof(RectConverter))]
		public Rect viewRect {
			get { return _viewRect; }
			set {
				_viewRect = value;
				cam.UCamera.aspect = value.width / value.height;
				cam.UpdateRenderTexture();
			}
		}
		
		public Settings_FPSLimiter FPSLimiter { get; private set; } = new Settings_FPSLimiter();
		public Settings_Smoothfollow Smoothfollow { get; private set; } = new Settings_Smoothfollow();
		public Settings_ModmapExtensions ModmapExtensions { get; private set; } = new Settings_ModmapExtensions();
		public Settings_Follow360 Follow360 { get; private set; } = new Settings_Follow360();


		[JsonConverter(typeof(Vector3Converter))]
		public Vector3 targetPos = new Vector3(0, 1.5f, -1.5f);
		[JsonConverter(typeof(Vector3Converter))]
		public Vector3 targetRot = new Vector3(3f, 0, 0);
	}
}