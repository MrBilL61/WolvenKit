using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DynamicData;
using DynamicData.Kernel;
using HelixToolkit.SharpDX.Core;
using HelixToolkit.Wpf.SharpDX;
using Microsoft.Win32;
using WolvenKit.App.Controllers;
using WolvenKit.App.Extensions;
using WolvenKit.App.Helpers;
using WolvenKit.App.Models;
using WolvenKit.App.Services;
using WolvenKit.App.ViewModels.Shell;
using WolvenKit.Common.Interfaces;
using WolvenKit.Common.PhysX;
using WolvenKit.Common.Services;
using WolvenKit.Core.Extensions;
using WolvenKit.Core.Interfaces;
using WolvenKit.Core.Services;
using WolvenKit.Modkit.RED4;
using WolvenKit.Modkit.RED4.Tools;
using WolvenKit.RED4.Archive.Buffer;
using WolvenKit.RED4.Archive.CR2W;
using WolvenKit.RED4.Archive.IO;
using WolvenKit.RED4.CR2W;
using WolvenKit.RED4.Types;
using IMaterial = WolvenKit.RED4.Types.IMaterial;
using KeyEventArgs = System.Windows.Forms.KeyEventArgs;
using Material = WolvenKit.App.Models.Material;

namespace WolvenKit.App.ViewModels.Documents;

public partial class RDTMeshViewModel : RedDocumentTabViewModel
{
    private readonly ISettingsManager _settingsManager;
    private readonly IGameControllerFactory _gameController;
    private readonly ILoggerService _loggerService;
    private readonly IModTools _modTools;
    private readonly GeometryCacheService _geometryCacheService;
    private readonly IModifierViewStateService _modifierSvc;

    protected readonly RedBaseClass? _data;

    private readonly Dictionary<string, LoadableModel> _modelList = new();
    private readonly Dictionary<string, SlotSet> _slotSets = new();

    private const int s_distanceCameraUnits = 145;
    private const double s_cameraUpDirectionFactor = 0.7;
    private const int s_cameraAnimationTime = 400;


    #region ctor

    public RDTMeshViewModel(RedDocumentViewModel parent, string header,
        ISettingsManager settingsManager,
        IGameControllerFactory gameController,
        ILoggerService loggerService,
        IModTools modTools,
        GeometryCacheService geometryCacheService,
        IModifierViewStateService modifierSvc
    ) : base(parent, header)
    {
        _loggerService = loggerService;
        _settingsManager = settingsManager;
        _gameController = gameController;
        _modTools = modTools;
        _geometryCacheService = geometryCacheService;
        _modifierSvc = modifierSvc;

        _modifierSvc.ModifierStateChanged += ModifierStateChanged;

        Parent = parent;
    }

    private void ModifierStateChanged() => IsCtrlKeyPressed = _modifierSvc.IsCtrlKeyPressed;

    // TODO refactor this into inherited viewmodels

    public RDTMeshViewModel(CMesh data, RedDocumentViewModel file,
        ISettingsManager settingsManager,
        IGameControllerFactory gameController,
        ILoggerService loggerService,
        IModTools modTools,
        GeometryCacheService geometryCacheService,
        IModifierViewStateService modifierSvc)
        : this(file, MeshViewHeaders.MeshPreview, settingsManager, gameController, loggerService, modTools,
            geometryCacheService, modifierSvc)
    {
        _data = data;
    }

    public RDTMeshViewModel(worldStreamingSector data, RedDocumentViewModel file,
        ISettingsManager settingsManager,
        IGameControllerFactory gameController,
        ILoggerService loggerService,
        IModTools modTools,
        GeometryCacheService geometryCacheService,
        IModifierViewStateService modifierSvc)
        : this(file, MeshViewHeaders.SectorPreview, settingsManager, gameController, loggerService, modTools,
            geometryCacheService, modifierSvc)
    {
        _data = data;
    }

    public RDTMeshViewModel(worldStreamingBlock data, RedDocumentViewModel file,
        ISettingsManager settingsManager,
        IGameControllerFactory gameController,
        ILoggerService loggerService,
        IModTools modTools,
        GeometryCacheService geometryCacheService,
        IModifierViewStateService modifierSvc)
        : this(file, MeshViewHeaders.AllSectorPreview, settingsManager, gameController, loggerService, modTools,
            geometryCacheService, modifierSvc)
    {
        _data = data;
    }

    public RDTMeshViewModel(entEntityTemplate ent, RedDocumentViewModel file,
        ISettingsManager settingsManager,
        IGameControllerFactory gameController,
        ILoggerService loggerService,
        IModTools modTools,
        GeometryCacheService geometryCacheService,
        IModifierViewStateService modifierSvc)
        : this(file, MeshViewHeaders.EntityPreview, settingsManager, gameController, loggerService, modTools,
            geometryCacheService, modifierSvc)
    {
        _data = ent;
    }

    public string? SelectedNodeIndex;
    
    

    public override void Load()
    {
        if (IsLoaded)
        {
            return;
        }

        appearanceWasAutoGenerated = false;

        materialWasAutoGenerated = false;
        
        try
        {
            foreach (var res in Parent.Cr2wFile.EmbeddedFiles)
            {
                if (!Parent.Files.ContainsKey(res.FileName))
                {
                    Parent.Files.Add(res.FileName, new CR2WFile()
                    {
                        RootChunk = res.Content
                    });
                }
            }

            EffectsManager = new DefaultEffectsManager();

            //EnvironmentMap = TextureModel.Create(Path.Combine(ISettingsManager.GetTemp_OBJPath(), "Cubemap_Grandcanyon.dds"));
            Camera = new HelixToolkit.Wpf.SharpDX.PerspectiveCamera()
            {
                FarPlaneDistance = 1E+8,
                LookDirection = new Vector3D(1f, -1f, -1f)
            };

            switch (_data)
            {
                case CMesh:
                    RenderMesh();
                    break;
                case worldStreamingSector:
                {
                    PanelVisibility.ShowSelectionPanel = true;
                    PanelVisibility.ShowToggleCollision = true;
                    var app = new Appearance(Path.GetFileNameWithoutExtension(Parent.ContentId).Replace("-", "_"));

                    Appearances.Add(app);
                    SelectedAppearance = app;

                    RenderSectorSolo();
                    break;
                }
                case worldStreamingBlock:
                    PanelVisibility.ShowSearchPanel = true;
                    PanelVisibility.ShowToggleCollision = true;
                    PanelVisibility.ShowAddSectors = true;

                    RenderBlockSolo();
                    break;
                case entEntityTemplate:
                    PanelVisibility.ShowExportEntity = true;

                    try
                    {
                        RenderEntitySolo();
                    }
                    catch (Exception e)
                    {
                        _loggerService.Error(
                            $"Failed to render entity. Please ensure that all your component names are unique. If they are, open a ticket: \n${e}");
                    }

                    break;
                default:
                    break;
            }

        }
        catch (Exception ex)
        {
            Parent.GetLoggerService().Error(ex);
        }
        IsLoaded = true;
    }

    public override void Unload()
    {
        if (_data is not CMesh data)
        {
            return;
        }

        if (materialWasAutoGenerated)
        {
            data.MaterialEntries.Clear();
            data.PreloadLocalMaterialInstances.Clear();
        }

        if (appearanceWasAutoGenerated)
        {
            data.Appearances.Clear();
        }
    }
    
    
    #endregion

    #region properties

    private EffectsManager? _effectsManager;
    private HelixToolkit.Wpf.SharpDX.Camera? _camera;

    public EffectsManager? EffectsManager
    {
        get => _effectsManager;
        set => SetProperty(ref _effectsManager, value);
    }

    public HelixToolkit.Wpf.SharpDX.Camera? Camera
    {
        get => _camera;
        private set => SetProperty(ref _camera, value);
    }

    public SceneNodeGroupModel3D GroupModel { get; set; } = new SceneNodeGroupModel3D();

    //public List<Element3D> ModelGroup { get; set; } = new();
    public SmartElement3DCollection ModelGroup { get; set; } = new();

    public TextureModel? EnvironmentMap { get; set; }

    public bool IsRendered;

    public PanelVisibility PanelVisibility { get; set; } = new();

    [ObservableProperty] private ImageSource? _image;

    [ObservableProperty] private object? _selectedItem;

    [ObservableProperty] private string? _loadedModelPath;

    [ObservableProperty] private List<LoadableModel> _models = new();

    [ObservableProperty] private Dictionary<string, Rig> _rigs = new();

    [ObservableProperty] private List<Appearance> _appearances = new();

    [ObservableProperty] private Appearance? _selectedAppearance;

    [ObservableProperty] private bool _isCtrlKeyPressed;

    [ObservableProperty] private bool _isLoaded;

    public bool CtrlKeyPressed { get; set; }

    public bool ShiftKeyPressed { get; set; }

    // We need to hold a copy of the property to avoid exceptions while filtering 
    public List<Element3D> SelectedModelGroup
    {
        get => SelectedAppearance?.ModelGroup.ToList() ?? [];
        set
        {
            if (SelectedAppearance is null)
            {
                return;
            }
            
            var newElems = new SmartElement3DCollection();
            value.ForEach(v => newElems.Add(v));
            SelectedAppearance.ModelGroup = newElems;
            OnPropertyChanged(new PropertyChangedEventArgs(nameof(SelectedModelGroup)));
        }
    }

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SelectedAppearance) && SelectedAppearance?.ModelGroup is not null)
        {
            SelectedModelGroup = SelectedAppearance.ModelGroup.ToList();
        }

        base.OnPropertyChanged(e);
    }

    #endregion

    #region commands

    [RelayCommand]
    private void ExportEntity()
    {
        if (SelectedAppearance is null)
        {
            return;
        }
        if (SelectedAppearance.AppearanceName is null)
        {
            return;
        }

        var dlg = new SaveFileDialog
        {
            FileName = Path.GetFileNameWithoutExtension(Parent.RelativePath) + ".glb",
            Filter = "GLB files (*.glb)|*.glb|All files (*.*)|*.*"
        };

        if (!dlg.ShowDialog().GetValueOrDefault())
        {
            return;
        }

        var outFile = new FileInfo(dlg.FileName);
        // will only use archive files (for now)
        var appearanceName = SelectedAppearance.AppearanceName.NotNull();
        if (_modTools.ExportEntity(Parent.Cr2wFile, appearanceName, outFile))
        {
            Parent.GetLoggerService().Success($"Entity with appearance '{appearanceName}'exported: {dlg.FileName}");
        }
        else
        {
            Parent.GetLoggerService().Error($"Error exporting entity with appearance '{appearanceName}'");
        }
    }

    [RelayCommand]
    private void ExtractShaders()
    {
        if (_settingsManager.CP77ExecutablePath is null)
        {
            return;
        }

        ShaderCacheReader.ExtractShaders(new FileInfo(_settingsManager.CP77ExecutablePath), ISettingsManager.GetTemp_OBJPath());
    }

    private bool _isCollisionRendered = true;

    [RelayCommand]
    public void ToggleCollision()
    {
        if (SelectedAppearance is null)
        {
            return;
        }

        _isCollisionRendered = !_isCollisionRendered;

        foreach (var element in SelectedAppearance.ModelGroup)
        {
            SetRenderingState(element);
        }
    }

    private void SetRenderingState(Element3D element)
    {
        if (element is GroupModel3DExt groupModel)
        {
            if (groupModel.Text != null && groupModel.Text.StartsWith("collisionNode_"))
            {
                groupModel.IsRendering = _isCollisionRendered;
                return;
            }

            foreach (var child in groupModel.Children)
            {
                SetRenderingState(child);
            }
        }

        if (element is not SubmeshComponent submesh || submesh.Text == null || !submesh.Text.StartsWith("collisionNode_"))
        {
            return;
        }

        submesh.IsRendering = _isCollisionRendered;
    }

    #endregion

    #region methods

    private bool appearanceWasAutoGenerated = false;

    private bool materialWasAutoGenerated = false;
    
    public void RenderMesh()
    {
        // TODO [mana] does IsLoadingMaterials check work as expected here?
        if (IsRendered && !IsLoadingMaterials)
        {
            return;
        }
        IsRendered = true;
        if (_data is not CMesh data)
        {
            return;
        }
        var materials = new Dictionary<string, Material>();

        var localList = data.LocalMaterialBuffer.RawData?.Buffer.Data as CR2WList ?? null;

        // If there are no local materials defined that we can use to render the mesh, generate one
        if (data.MaterialEntries.Count == 0 || data.MaterialEntries.All((entry) => !entry.IsLocalInstance))
        {
            materialWasAutoGenerated = true;
            data.MaterialEntries.Add(new CMeshMaterialEntry() { Name = (CName)"default (generated)", Index = 0, IsLocalInstance = true });
            var material = new CMaterialInstance()
            {
                BaseMaterial = new CResourceReference<RED4.Types.IMaterial>(@"engine\materials\metal_base.remt")
            };
            material.Values.Add(new CKeyValuePair("BaseColor", (ResourcePath)@"base\surfaces\materials\default\debug_d.xbm"));
            data.PreloadLocalMaterialInstances.Add(material);
        }

        if (data.Appearances.Count == 0)
        {
            appearanceWasAutoGenerated = true;
            var app = new meshMeshAppearance();

            for (var i = 0; i < 21; i++)
            {
                app.ChunkMaterials.Add((CName)"default (generated)");
            }

            data.Appearances.Add(new CHandle<meshMeshAppearance>(app));
        }
        
        foreach (var me in data.MaterialEntries)
        {
            var name = GetUniqueMaterialName(me.Name.ToString().NotNull(), data);
            if (!me.IsLocalInstance)
            {
                materials.TryAdd(name, new Material(name));
                continue;
            }
            CMaterialInstance? inst = null;
            
            if (localList != null && localList.Files.Count > me.Index)
            {
                inst = localList.Files[me.Index].RootChunk as CMaterialInstance;
            }
            else
            {
                // No further materials are defined (user was lazy)
                if (me.Index >= data.PreloadLocalMaterialInstances.Count)
                {
                    Parent.GetLoggerService().Warning($"No material with index {me.Index} found. Not checking further entries…");
                    break;
                }

                inst = data.PreloadLocalMaterialInstances[me.Index]?.GetValue() as CMaterialInstance;
            }

            if (inst == null)
            {
                continue;
            }
          
            var material = new Material(name)
            {
                Instance = inst,
            };

            foreach (var pair in inst.Values)
            {
                var k = pair.Key.ToString().NotNull();
                material.Values[k] = pair.Value;
            }

            materials[name] = material;
        }

        var appIndex = 0;
        foreach (var handle in data.Appearances)
        {
            if (handle.GetValue() is not meshMeshAppearance mmapp)
            {
                appIndex++;
                continue;
            }

            var appMaterials = new List<Material>();

            foreach (var materialName in mmapp.ChunkMaterials)
            {
                var name = GetUniqueMaterialName(materialName.ToString().NotNull(), data);
                appMaterials.Add(materials.TryGetValue(name, out var material) ? material : new Material(name));
            }

            var appearance = new Appearance(mmapp.Name.ToString().NotNull());

            var model = new LoadableModel(Path.GetFileNameWithoutExtension(Parent.ContentId).Replace("-", "_").Replace(".", "_"))
            {
                MeshFile = Parent.Cr2wFile,
                AppearanceIndex = appIndex,
                AppearanceName = mmapp.Name,
                Materials = appMaterials,
                IsEnabled = true
            };
            appearance.Models.Add(model);
            appearance.BindableModels.Add(model);
            foreach (var material in materials.Values)
            {
                appearance.RawMaterials[material.Name] = material;
            }

            model.Meshes = MakeMesh(data, ulong.MaxValue, model.AppearanceIndex);

            var materialIndex = 0;
            foreach (var m in model.Meshes)
            {
                if (!appearance.LODLUT.TryGetValue(m.LOD, out var value))
                {
                    value = [];
                    appearance.LODLUT[m.LOD] = value;
                }

                // Ensure materialIndex is within the bounds of a.RawMaterials.Keys
                if (materialIndex >= appearance.RawMaterials.Keys.Count)
                {
                    materialIndex = 0; // Or handle this scenario as appropriate for your application
                }

                if (appearance.RawMaterials.Keys.Count > 0)
                {
                    var materialKey = appearance.RawMaterials.Keys.ElementAt(materialIndex);
                    m.MaterialName ??= materialKey;

                    value.Add(m);
                }
            }

            appearance.ModelGroup.AddRange(AddMeshesToRiggedGroups(appearance));

            Appearances.Add(appearance);
            appIndex++;
        }

        SelectedAppearance = Appearances.FirstOrDefault();
    }


    public GroupModel3D GroupFromRigBone(Rig rig, RigBone bone, Dictionary<string, GroupModel3D> groups)
    {
        var group = new GroupModel3DExt()
        {
            Name = bone.Name,
            Text = bone.Name,
            Transform = new MatrixTransform3D(bone.Matrix.ToMatrix3D())
        };
        foreach (var child in bone.Children)
        {
            group.Children.Add(GroupFromRigBone(rig, child, groups));
        }
        groups.Add(rig.Name + ":" + bone.Name, group);
        return group;
    }


    public GroupModel3D GroupFromModel(LoadableModel model)
    {
        var group = new MeshComponent();
        try
        {
            group.Name = !string.IsNullOrEmpty(model.Name) && char.IsDigit(model.Name[0]) ? $"_{model.Name}" : $"{model.Name}";
            group.Text = model.Name;
            group.AppearanceName = model.AppearanceName;
            group.Transform = model.Transform;
            group.IsRendering = model.IsEnabled;
            group.DepotPath = model.DepotPath;
        }
        catch (Exception ex)
        {
            Console.Write(ex);
        }

        foreach (var mesh in model.Meshes)
        {
            group.Children.Add(mesh);
        }

        foreach (var child in model.Models)
        {
            group.Children.Add(GroupFromModel(child));
        }
        return group;
    }

    private List<Element3D> AddMeshesToRiggedGroups(Appearance app)
    {
        var groups = new Dictionary<string, GroupModel3D>();
        var modelGroups = new List<Element3D>();
        foreach (var (name, rig) in Rigs)
        {
            if (name is "deformations" or "root")
            {
                continue;
            }
            var group = new GroupModel3DExt()
            {
                Name = $"{rig.Name}",
                Text = $"{rig.Name}",
                Transform = new MatrixTransform3D(rig.Matrix.ToMatrix3D())
            };
            group.Children.Add(GroupFromRigBone(rig, rig.Bones[0], groups));
            groups.Add(name, group);
            modelGroups.Add(group);
        }

        foreach (var model in app.BindableModels)
        {
            var group = GroupFromModel(model);

            if (model.BindName is null || model.SlotName is null or "None")
            {
                modelGroups.Add(group);
                continue;
            }

            if (_slotSets.TryGetValue(model.BindName, out var slotBind))
            {
                if (model.SlotName != null && slotBind.Slots.TryGetValue(model.SlotName, out var slot) &&
                    groups.ContainsKey(slotBind.BindName + ":" + slot))
                {
                    groups[slotBind.BindName + ":" + slot].Children.Add(group);
                    
                }
            }
            else if (groups.TryGetValue(model.BindName, out var modelBind))
            {
                modelBind.Children.Add(group);
            }
            else
            {
                modelGroups.Add(group);
            }
        }
        return modelGroups;
    }

    private IEnumerable<LoadableModel> LoadPartsValues(appearanceAppearanceDefinition appDef)
    {
        if (appDef.PartsValues.Count is 0)
        {
            return [];
        }

        List<LoadableModel> ret = [];
        var appModels = new Dictionary<string, LoadableModel>();

        foreach (var appDefPartsValue in appDef.PartsValues)
        {
            if (appDefPartsValue is not appearanceAppearancePart partValue ||
                partValue.Resource.DepotPath.GetResolvedText() is not string path || path == "")
            {
                continue;
            }

            List<entIComponent> components = [];
            try
            {
                var meshEntityFile = Parent.GetFileFromDepotPathOrCache(path);
                if (meshEntityFile?.RootChunk is not entEntityTemplate entityTemplate)
                {
                    continue;
                }

                components.AddRange(entityTemplate.Components);
            }
            catch

            {
                Parent.GetLoggerService().Warning($"Failed to read partsValue from {path}");
                continue;
            }

            foreach (var entityTemplateComponent in components)
            {
                if (entityTemplateComponent is not IRedMeshComponent irm || irm.Name.GetResolvedText() is not string s || s == "")
                {
                    continue;
                }

                ProcessComponents(entityTemplateComponent, appModels);
                if (appModels.TryGetValue(s, out var model))
                {
                    ret.Add(model);
                }
            }
        }

        return ret;
    }


    private void LoadPartsOverrides(appearanceAppearanceDefinition appDef, List<LoadableModel> aModels)
    {
        if (appDef.PartsOverrides.Count == 0)
        {
            return;
        }

        foreach (var partOverride in appDef.PartsOverrides)
        {
            if (partOverride.ComponentsOverrides.Count == 0)
            {
                continue;
            }

            var hasComponentOverride = partOverride.PartResource.DepotPath.GetResolvedText() is string and not "";

            foreach (var compOverride in partOverride.ComponentsOverrides)
            {
                if (compOverride.ComponentName == CName.Empty)
                {
                    continue;
                }

                // Set ChunkMasks and appearance name from PartsOverride
                foreach (var loadableModel in aModels.Where((m) => m.ComponentName == compOverride.ComponentName).ToList())
                {
                    loadableModel.ChunkMask = compOverride.ChunkMask;
                    if (hasComponentOverride && compOverride.MeshAppearance != CName.Empty)
                    {
                        loadableModel.AppearanceName = compOverride.MeshAppearance;
                    }
                }
            }
        }
    }


    private List<LoadableModel> LoadMeshes(IList<RedBaseClass>? chunks)
    {
        if (chunks == null || chunks.Count == 0)
        {
            return [];
        }

        var appModels = new Dictionary<string, LoadableModel>();

        foreach (var component in chunks)
        {
            ProcessComponents(component, appModels);
        }

        var list = new List<LoadableModel>();

        foreach (var model in appModels.Values)
        {
            var matrix = new SeparateMatrix();
            //GetResolvedMatrix(model, ref matrix, appModels);
            model.Transform = new MatrixTransform3D(model.Matrix.ToMatrix3D());
            if (model.Name.Contains("shadow") || model.Name.Contains("AppearanceProxyMesh") || model.Name.Contains("cutout") ||
                model.Name == "")
            {
                model.IsEnabled = false;
            }

            list.Add(model);
        }

        if (list.Count != 0)
        {
            list.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.Ordinal));
        }


        return list;
    }

    private Material defaultMaterial = new("default")
    {
        Instance = new CMaterialInstance() { BaseMaterial = new CResourceReference<IMaterial>(@"engine\materials\metal_base.remt") }
    };

    private void ProcessComponents(RedBaseClass component, Dictionary<string, LoadableModel> appModels)
    {
        var scale = new Vector3() { X = 1, Y = 1, Z = 1 };
        var depotPath = ResourcePath.Empty;
        var componentName = "";
        var enabled = true;
        var meshApp = "default";
        var chunkMask = 18446744073709551615;
        var chunkList = new List<bool>(new bool[64]);

        if (component is entMeshComponent emc)
        {
            scale = emc.VisualScale;
            enabled = emc.IsEnabled;
        }

        if (component is IRedMeshComponent mc)
        {
            depotPath = mc.Mesh.DepotPath;
            meshApp = mc.MeshAppearance;
            chunkMask = mc.ChunkMask;
            componentName = mc.Name;
        }

        var enabledChunks = new ObservableCollection<int>();

        for (var i = 0; i < 64; i++)
        {
            chunkList[i] = (chunkMask & 1UL << i) > 0;
            if (chunkList[i])
            {
                enabledChunks.Add(i);
            }
        }

        if (component is not entIPlacedComponent epc || depotPath == ResourcePath.Empty || depotPath.GetRedHash() == 0)
        {
            return;
        }

        var meshFile = Parent.GetFileFromDepotPathOrCache(depotPath);

        if (meshFile is not { RootChunk: CMesh mesh })
        {
            Parent.GetLoggerService().Warning($"Couldn't find mesh file: {depotPath} / {depotPath.GetRedHash()}");
            return;
        }

        var matrix = ToSeparateMatrix(epc.LocalTransform);

        string? bindName = null, slotName = null;
        if ((epc.ParentTransform?.GetValue() ?? null) is entHardTransformBinding ehtb)
        {
            bindName = ehtb.BindName;
            slotName = ehtb.SlotName;
        }

        matrix.Scale(ToScaleVector3D(scale));

        var materials = new Dictionary<string, Material>();

        var localList = mesh.LocalMaterialBuffer.RawData?.Buffer.Data as CR2WList ?? null;

        foreach (var me in mesh.MaterialEntries)
        {
            var name = GetUniqueMaterialName(me.Name.ToString().NotNull(), mesh);
            if (!me.IsLocalInstance)
            {
                materials.TryAdd(name, new Material(name));
                continue;
            }

            CMaterialInstance? inst = null;

            if (localList != null && localList.Files.Count > me.Index)
            {
                inst = (CMaterialInstance)localList.Files[me.Index].RootChunk;
            }
            else if (mesh.PreloadLocalMaterialInstances.Count > me.Index)
            {
                //foreach (var pme in data.PreloadLocalMaterialInstances)
                //{
                //inst = (CMaterialInstance)pme.GetValue();
                //}
                inst = (CMaterialInstance?)mesh.PreloadLocalMaterialInstances[me.Index]?.GetValue();
            }

            //CMaterialInstance bm = null;
            //if (File.GetFileFromDepotPathOrCache(inst.BaseMaterial.DepotPath) is var file)
            //{
            //    bm = (CMaterialInstance)file.RootChunk;
            //}
            if (inst is null)
            {
                _loggerService.Error($"Failed to load material {name}! Check your mesh materials!");
                materials[name] = defaultMaterial;
                continue;
            }

            var material = new Material(name) { Instance = inst, };

            foreach (var pair in inst.Values)
            {
                var k = pair.Key.ToString().NotNull();
                material.Values[k] = pair.Value;
            }

            materials[name] = material;
        }

        if (materials.Count == 0)
        {
            materials.Add("default", defaultMaterial);
        }
        var apps = new List<string>();
        foreach (var handle in mesh.Appearances)
        {
            var app = handle.GetValue();
            if (app is meshMeshAppearance mmapp)
            {
                apps.Add(mmapp.Name.ToString().NotNull());
            }
        }

        if (apps.Count == 0)
        {
            apps.Add("default");
        }

        var appIndex = 0;
        ArgumentNullException.ThrowIfNull(meshApp);
        if (meshApp != "default" && apps.IndexOf(meshApp) is var index && index != -1)
        {
            appIndex = index;
        }

        var appMaterials = new List<Material>();

        foreach (var handle in mesh.Appearances)
        {
            var app = handle.GetValue();
            if (app is not meshMeshAppearance mmApp ||
                (mmApp.Name != meshApp && (meshApp != "default" || mesh.Appearances.IndexOf(handle) != 0)))
            {
                continue;
            }

            foreach (var m in mmApp.ChunkMaterials)
            {
                var name = GetUniqueMaterialName(m.ToString().NotNull(), mesh);
                if (materials.TryGetValue(name, out var material))
                {
                    appMaterials.Add(material);
                }
                else
                {
                    appMaterials.Add(new Material(name));
                }
            }

            break;
        }

        if (appMaterials.Count == 0)
        {
            appMaterials.Add(defaultMaterial);
        }

        var modelName = (epc.Name.ToString() ?? "").Replace(".", "");
        var counter = 0;
        while (appModels.ContainsKey(modelName))
        {
            counter += 1;
            modelName = $"{(epc.Name.ToString() ?? "").Replace(".", "")}_DUPLICATE_{counter}";
        }

        var model = new LoadableModel(modelName)
        {
            ComponentName = (CName)(componentName ?? ""),
            MeshFile = meshFile,
            AppearanceIndex = appIndex,
            AppearanceName = meshApp,
            Matrix = matrix,
            Materials = appMaterials,
            IsEnabled = enabled,
            BindName = bindName,
            SlotName = slotName,
            ChunkMask = chunkMask,
            ChunkList = chunkList,
            EnabledChunks = enabledChunks,
            DepotPath = depotPath
        };

        appModels.Add(modelName, model);
    }

    public void GetResolvedMatrix(IBindable bindable, ref SeparateMatrix matrix, Dictionary<string, LoadableModel> models)
    {
        matrix.Append(bindable.Matrix);

        if (bindable.BindName == null)
        {
            return;
        }

        if (bindable is not LoadableModel)
        {
            if (Rigs.TryGetValue(bindable.BindName, out var value))
            {
                GetResolvedMatrix(value, ref matrix, models);
            }

            return;
        }

        if (models.TryGetValue(bindable.BindName, out var boundModel))
        {
            GetResolvedMatrix(boundModel, ref matrix, models);
        }
        else if (_slotSets.TryGetValue(bindable.BindName, out var value))
        {
            if (bindable.SlotName != null && value.Slots.TryGetValue(bindable.SlotName, out var slot))
            {
                var bindname = value.BindName;
                if (bindname is not null && Rigs.ContainsKey(bindname))
                {
                    var rigBone = Rigs[bindname].Bones.Where(x => x.Name == slot).FirstOrDefault(defaultValue: null);

                    while (rigBone != null)
                    {
                        matrix.AppendPost(rigBone.Matrix);
                        rigBone = rigBone.Parent as RigBone;
                    }
                }
            }

            // not sure this does anything anywhere
            GetResolvedMatrix(value, ref matrix, models);
        }
    }

   

    public override ERedDocumentItemType DocumentItemType => ERedDocumentItemType.W2rcBuffer;



    public static Matrix3D ToMatrix3D(QsTransform qs)
    {
        var matrix = new Matrix3D();
        matrix.Rotate(ToQuaternion(qs.Rotation));
        matrix.Translate(ToVector3D(qs.Translation));
        matrix.Scale(ToScaleVector3D(qs.Scale));
        return matrix;
    }

    public static Matrix3D ToMatrix3D(WorldTransform wt)
    {
        var matrix = new Matrix3D();
        matrix.Rotate(ToQuaternion(wt.Orientation));
        matrix.Translate(ToVector3D(wt.Position));
        return matrix;
    }

    public static SeparateMatrix ToSeparateMatrix(QsTransform qs)
    {
        var matrix = new SeparateMatrix();
        matrix.Rotate(ToQuaternion(qs.Rotation));
        matrix.Translate(ToVector3D(qs.Translation));
        matrix.Scale(ToScaleVector3D(qs.Scale));
        return matrix;
    }

    public static SeparateMatrix ToSeparateMatrix(WorldTransform wt)
    {
        var matrix = new SeparateMatrix();
        matrix.Rotate(ToQuaternion(wt.Orientation));
        matrix.Translate(ToVector3D(wt.Position));
        return matrix;
    }

    //public static System.Windows.Media.Media3D.Quaternion ToQuaternion(RED4.Types.Quaternion q) => new System.Windows.Media.Media3D.Quaternion(q.I, q.J, q.K, q.R);

    public static System.Windows.Media.Media3D.Quaternion ToQuaternion(RED4.Types.Quaternion q) => new(q.I, q.K, -q.J, q.R);
    public static System.Windows.Media.Media3D.Quaternion ToQuaternionOG(RED4.Types.Quaternion q) => new(q.I, q.J, q.K, q.R);

    //public static Vector3D ToVector3D(WorldPosition v) => new Vector3D(v.X, v.Y, v.Z);

    //public static Vector3D ToVector3D(Vector4 v) => new Vector3D(v.X, v.Y, v.Z);

    //public static Vector3D ToVector3D(Vector3 v) => new Vector3D(v.X, v.Y, v.Z);

    public static Vector3D ToVector3D(WorldPosition v) => new((float)v.X, (float)v.Z, -(float)v.Y);

    public static Vector3D ToVector3D(Vector4 v) => new(v.X, v.Z, -v.Y);

    public static Vector3D ToVector3D(Vector3 v) => new(v.X, v.Z, -v.Y);

    public static Vector3D ToScaleVector3D(Vector4 v) => new(v.X, v.Z, v.Y);

    public static Vector3D ToScaleVector3D(Vector3 v) => new(v.X, v.Z, v.Y);

    public static Matrix3D ToMatrix3D(CMatrix matrix) => new(matrix.X.X, matrix.Y.X, matrix.Z.X, matrix.W.X,
                                                                      matrix.X.Y, matrix.Y.Y, matrix.Z.Y, matrix.W.Y,
                                                                      matrix.X.Z, matrix.Y.Z, matrix.Z.Z, matrix.W.Z,
                                                                      matrix.X.W, matrix.Y.W, matrix.Z.W, matrix.W.W);
    //public static Matrix3D ToMatrix3D(CMatrix matrix) => new Matrix3D(matrix.X.X, matrix.Z.X, -matrix.Y.X, matrix.W.X,
    //                                                                  matrix.X.Z, matrix.Z.Z, -matrix.Y.Z, matrix.W.Z,
    //                                                                  -matrix.X.Y, -matrix.Z.Y, matrix.Y.Y, -matrix.W.Y,
    //                                                                  matrix.X.W, matrix.Z.W, -matrix.Y.W, matrix.W.W);
    //public static Matrix3D ToMatrix3D(CMatrix matrix) => new Matrix3D(matrix.W.W, matrix.X.W, matrix.Y.W, matrix.Z.W,
    //                                                                  matrix.W.X, matrix.X.X, matrix.Y.X, matrix.Z.X,
    //                                                                  matrix.W.Y, matrix.X.Y, matrix.Y.Y, matrix.Z.Y,
    //                                                                  matrix.W.Z, matrix.X.Z, matrix.Y.Z, matrix.Z.Z);
    //public static Matrix3D ToMatrix3D(CMatrix matrix) => new Matrix3D(matrix.X.X, matrix.X.Y, matrix.X.Z, matrix.X.W,
    //                                                                  matrix.Y.X, matrix.Y.Y, matrix.Y.Z, matrix.Y.W,
    //                                                                  matrix.Z.X, matrix.Z.Y, matrix.Z.Z, matrix.Z.W,
    //                                                                  matrix.W.X, matrix.W.Y, matrix.W.Z, matrix.W.W);

    public void CenterCameraToCoord(Vector3 coord)
    {
        Camera?.AnimateTo(
                new System.Windows.Media.Media3D.Point3D(coord.X, coord.Z + s_distanceCameraUnits, -coord.Y + s_distanceCameraUnits),
                new Vector3D(0, -s_distanceCameraUnits, -s_distanceCameraUnits),
                new Vector3D(0, s_cameraUpDirectionFactor, -s_cameraUpDirectionFactor),
                s_cameraAnimationTime);
    }

    public void MouseDown3D(object sender, RoutedEventArgs e)
    {
        if (e is not MouseDown3DEventArgs args || args.HitTestResult == null)
        {
            return;
        }

        var mouseButtonEventArgs = (MouseButtonEventArgs)args.OriginalInputEventArgs;
        MouseDown3DSector(sender, args, mouseButtonEventArgs);
        MouseDown3DBlock(sender, args, mouseButtonEventArgs);

        CommonMouseDownEvents(args.HitTestResult.ModelHit, mouseButtonEventArgs);
    }

    public event EventHandler<string?>? OnSectorNodeSelected;

    
    private void CommonMouseDownEvents(object modelHit, MouseButtonEventArgs mouseButtonEventArgs)
    {
        if (mouseButtonEventArgs.LeftButton != MouseButtonState.Pressed || modelHit is not SubmeshComponent
            {
                Parent: MeshComponent { Parent: MeshComponent mesh }
            })
        {
            return;
        }

        List<string> loggerArgs = [];
        if (!string.IsNullOrEmpty(mesh.WorldNodeIndex))
        {
            loggerArgs.Add($"nodes[{mesh.WorldNodeIndex}] (Type: \"{mesh.WorldNodeType}\", nodeDataIndices: [{mesh.WorldNodeDataIndices}]");
        }

        if (mesh.CollisionActorId != null)
        {
            loggerArgs.Add($"Collision Actor: [{mesh.CollisionActorId}]");
        }

        if (modelHit is SubmeshComponent submesh)
        {
            if (submesh.CollisionActorId != null)
            {
                loggerArgs.Add($"Collision Actor: [{submesh.CollisionActorId}]");
            }

            if (CtrlKeyPressed)
            {

                //
                var v = submesh.BoundsSphereWithTransform.Center;
                // centre the view on the selected submesh2          
                Camera?.LookAt(new System.Windows.Media.Media3D.Point3D(v.X, v.Y, v.Z), 100);
                loggerArgs.Add($"Mesh Bounds Centre: {v.X}, {v.Y}, {v.Z}");
            }
            
            if (ShiftKeyPressed)
            {
                if (Camera != null )
                {
                    // Zoom to the selected submesh
                    var v = submesh.BoundsWithTransform;

                    // Calculate the center of the bbox
                    SharpDX.Vector3 bboxCenter = new SharpDX.Vector3(v.Center.X, v.Center.Y, v.Center.Z);

                    // Calculate the extent of the bbox along each axis
                    SharpDX.Vector3 bboxExtent = v.Maximum - v.Minimum;

                    // Calculate the maximum extent of the bbox
                    float maxExtent = Math.Max(bboxExtent.X, Math.Max(bboxExtent.Y, bboxExtent.Z));

                    // Calculate the distance from the camera to ensure the entire bbox is visible
                    float distance = maxExtent / (float)Math.Tan(SharpDX.MathUtil.DegreesToRadians(45.0f) / 2); // Assuming 45 degree field of view

                    SharpDX.Vector3 CameraPos = new SharpDX.Vector3((float)Camera.Position.X, (float)Camera.Position.Y, (float)Camera.Position.Z);

                    // Calculate the new camera position and target
                    SharpDX.Vector3 newPosition = bboxCenter + SharpDX.Vector3.Normalize(CameraPos - bboxCenter) * distance;
                    SharpDX.Vector3 newDirection = SharpDX.Vector3.Normalize(bboxCenter - newPosition);
                    

                    Camera?.AnimateTo(
                    new System.Windows.Media.Media3D.Point3D(newPosition.X, newPosition.Y, newPosition.Z),
                    new Vector3D(newDirection.X, newDirection.Y, newDirection.Z),
                    new Vector3D(0,1,0),
                    s_cameraAnimationTime);
                }
            }
        }

        if (loggerArgs.Count == 0)
        {
            loggerArgs.Add("Mesh Name :");
        }
        
        

        
        Parent.GetLoggerService().Info(string.Join(", ", loggerArgs) + ") " + mesh.Text);

        OnSectorNodeSelected?.Invoke(this, mesh.WorldNodeIndex);
    }

    #endregion

    // TODO sort this

    #region meshhelper

    public Dictionary<string, PBRMaterial> Materials { get; set; } = new();

    public List<SubmeshComponent> MakeMesh(CMesh cMesh, ulong chunkMask = ulong.MaxValue, int appearanceIndex = 0)
    {
        if (cMesh.RenderResourceBlob == null || cMesh.RenderResourceBlob.Chunk is not rendRenderMeshBlob rendblob)
        {
            return [];
        }

        using var ms = new MemoryStream(rendblob.RenderBuffer.Buffer.GetBytes());

        var meshesinfo = MeshTools.GetMeshesinfo(rendblob, cMesh);

        var expMeshes = MeshTools.ContainRawMesh(ms, meshesinfo, false, ulong.MaxValue);

        var list = new List<SubmeshComponent>();

        var index = 0;
        foreach (var mesh in expMeshes)
        {
            ArgumentNullException.ThrowIfNull(mesh.positions);
            ArgumentNullException.ThrowIfNull(mesh.indices);
            ArgumentNullException.ThrowIfNull(mesh.normals);
            ArgumentNullException.ThrowIfNull(mesh.texCoords0);
            ArgumentNullException.ThrowIfNull(mesh.tangents);
            ArgumentNullException.ThrowIfNull(mesh.materialNames);

            var positions = new Vector3Collection(mesh.positions.Length);
            for (var i = 0; i < mesh.positions.Length; i++)
            {
                positions.Add(mesh.positions[i].ToVector3());
            }

            var indices = new IntCollection(mesh.indices.Length);
            for (var i = 0; i < mesh.indices.Length; i++)
            {
                indices.Add((int)mesh.indices[i]);
            }

            Vector3Collection normals;
            if (mesh.normals.Length > 0)
            {
                normals = new Vector3Collection(mesh.normals.Length);
                for (var i = 0; i < mesh.normals.Length; i++)
                {
                    normals.Add(mesh.normals[i].ToVector3());
                }
            }
            else
            {
                normals = new Vector3Collection(mesh.positions.Length);
                for (var i = 0; i < mesh.positions.Length; i++)
                {
                    normals.Add(new SharpDX.Vector3(0f, 1f, 0f));
                }
                //ComputeNormals(positions, indices, out normals);
            }

            Vector2Collection textureCoordinates;
            if (mesh.texCoords0.Length > 0)
            {
                textureCoordinates = new Vector2Collection(mesh.texCoords0.Length);
                for (var i = 0; i < mesh.texCoords0.Length; i++)
                {
                    textureCoordinates.Add(mesh.texCoords0[i].ToVector2Flip());
                }
            }
            else
            {
                textureCoordinates = new Vector2Collection(mesh.positions.Length);
                if (cMesh.Parameters[0].NotNull().Chunk is meshMeshParamTerrain mmpt)
                {
                    float xMax = 0, xMin = 0, yMin = 0, yMax = 0;
                    foreach (var chunk in mmpt.ChunkBoundingBoxes)
                    {
                        xMax = Math.Max(xMax, chunk.Max.X);
                        yMax = Math.Max(yMax, chunk.Max.Y);
                        xMin = Math.Min(xMin, chunk.Min.X);
                        yMin = Math.Min(yMin, chunk.Min.Y);
                    }
                    if (xMax > 1024f || xMin < -1024f)
                    {
                        xMax = 2048;
                        xMin = -2048;
                        yMax = 2048;
                        yMin = -2048;
                    }
                    if (xMax > 512f || xMin < -512f)
                    {
                        xMax = 1024;
                        xMin = -1024;
                        yMax = 1024;
                        yMin = -1024;
                    }
                    if (xMax > 256f || xMin < -256f)
                    {
                        xMax = 512;
                        xMin = -512;
                        yMax = 512;
                        yMin = -512;
                    }
                    else if (xMax > 128 || xMin < -128)
                    {
                        xMax = 256;
                        xMin = -256;
                        yMax = 256;
                        yMin = -256;
                    }
                    else
                    {
                        xMax = 128;
                        xMin = -128;
                        yMax = 128;
                        yMin = -128;
                    }
                    for (var i = 0; i < mesh.positions.Length; i++)
                    {
                        textureCoordinates.Add(new SharpDX.Vector2(
                            (mesh.positions[i].X - xMin) / (xMax - xMin),
                            1f - (mesh.positions[i].Z - yMin) / (yMax - yMin)
                        ));
                    }
                }
            }

            Vector3Collection tangents;
            if (mesh.tangents.Length > 0)
            {
                tangents = new Vector3Collection(mesh.tangents.Length);
                for (var i = 0; i < mesh.tangents.Length; i++)
                {
                    tangents.Add(mesh.tangents[i].ToVector3());
                }
            }
            else
            {
                MeshBuilder.ComputeTangents(positions, normals, textureCoordinates, indices, out tangents, out var bitangents);
            }

            var sm = new SubmeshComponent()
            {
                Name = $"submesh_{index:D2}_LOD_{meshesinfo.LODLvl[index]:D2}",
                Text = $"submesh_{index:D2}_LOD_{meshesinfo.LODLvl[index]:D2}",
                LOD = meshesinfo.LODLvl[index],
                IsRendering = (chunkMask & 1UL << index) > 0 && meshesinfo.LODLvl[index] == (SelectedAppearance?.SelectedLOD ?? 1),
                EnabledWithMask = (chunkMask & 1UL << index) > 0,
                //CullMode = SharpDX.Direct3D11.CullMode.Front,
                Geometry = new HelixToolkit.SharpDX.Core.MeshGeometry3D()
                {
                    Positions = positions,
                    Indices = indices,
                    Normals = normals,
                    TextureCoordinates = textureCoordinates,
                    Tangents = tangents
                },
                DepthBias = -index * 2
                //IsTransparent = true
            };

            if (mesh.materialNames.Length > appearanceIndex)
            {
                sm.MaterialName = GetUniqueMaterialName(mesh.materialNames[appearanceIndex], cMesh);
                sm.Material = SetupPBRMaterial(sm.MaterialName);
                if (sm.MaterialName.Contains("glass"))
                {
                    sm.DepthBias -= 10;
                    sm.IsTransparent = true;
                }
                if (sm.MaterialName.Contains("sticker") || sm.MaterialName.Contains("decal"))
                {
                    sm.DepthBias -= 15;
                    sm.IsTransparent = true;
                }
            }
            else
            {
                sm.Material = SetupPBRMaterial("DefaultMaterial");
            }
            list.Add(sm);
            index++;
        }

        return list;

    }

    public static void ComputeNormals(Vector3Collection positions, IntCollection triangleIndices, out Vector3Collection normals)
    {
        normals = new Vector3Collection(positions.Count);
        for (var i = 0; i < positions.Count; i++)
        {
            normals.Add(new SharpDX.Vector3(0f, 0f, 0f));
        }

        for (var j = 0; j < triangleIndices.Count; j += 3)
        {
            var index = triangleIndices[j + 2];
            var index2 = triangleIndices[j + 1];
            var index3 = triangleIndices[j];
            var right = positions[index];
            var left = positions[index2];
            var left2 = positions[index3];
            var first = left - right;
            var second = left2 - right;
            var value = CrossProduct(ref first, ref second);
            first.Normalize();
            second.Normalize();
            var scale = (float)Math.Acos(DotProduct(ref first, ref second));
            value.Normalize();
            normals[index] += scale * value;
            normals[index2] += scale * value;
            normals[index3] += scale * value;
        }

        for (var k = 0; k < normals.Count; k++)
        {
            var value2 = normals[k];
            value2.Normalize();
            normals[k] = value2;
        }
    }

    public static SharpDX.Vector3 CrossProduct(ref SharpDX.Vector3 first, ref SharpDX.Vector3 second) => SharpDX.Vector3.Cross(first, second);

    public static float DotProduct(ref SharpDX.Vector3 first, ref SharpDX.Vector3 second) => first.X * second.X + first.Y * second.Y + first.Z * second.Z;


    public PBRMaterial SetupPBRMaterial(string name, bool force = false)
    {
        if (Materials.ContainsKey(name) && !force)
        {
            return Materials[name];
        }

        PBRMaterial material;
        if (Materials.TryGetValue(name, out var cachedMaterial))
        {
            material = cachedMaterial;
        }
        else
        {
            material = new PBRMaterial()
            {
                EnableAutoTangent = true, RenderShadowMap = true, RenderEnvironmentMap = true,
                //EnableTessellation = true,
                //MaxDistanceTessellationFactor = 2,
                //MinDistanceTessellationFactor = 4
            };
            Materials[name] = material;
        }

        var (filename_b, filename_bn, filename_rm, filename_d, filename_n) = GetMaterialFilePathsFromCache(name);
        


        if (File.Exists(filename_d))
        {
            material.AlbedoMap = TextureModel.Create(filename_d);
            material.AlbedoColor = new SharpDX.Color4(1.0f, 1.0f, 1.0f, 1.0f);
        }
        else if (File.Exists(filename_b))
        {
            material.AlbedoMap = TextureModel.Create(filename_b);
            material.AlbedoColor = new SharpDX.Color4(1.0f, 1.0f, 1.0f, 1.0f);
        }
        else
        {
            material.AlbedoColor = new SharpDX.Color4(0.5f, 0.5f, 0.5f, 1f);
        }

        if (File.Exists(filename_n))
        {
            material.NormalMap = TextureModel.Create(filename_n);
            material.RenderNormalMap = true;
        }
        else if (File.Exists(filename_bn))
        {
            material.NormalMap = TextureModel.Create(filename_bn);
            material.RenderNormalMap = true;
        }

        if (File.Exists(filename_rm))
        {
            material.RoughnessMetallicMap = TextureModel.Create(filename_rm);
            material.RenderRoughnessMetallicMap = true;
            material.RoughnessFactor = 1f;
            material.MetallicFactor = 1f;
        }

        if (name.Contains("glass"))
        {
            material.AlbedoColor = new SharpDX.Color4(0.5f, 0.5f, 0.5f, 0.1f);
        }

        if (name == "decals")
        {
            //material.AlbedoColor = new SharpDX.Color4(0, 0, 0, 0.1f);
            //material.DisplacementMap = material.AlbedoMap;
        }
        return Materials[name];
    }

    /// <summary>
    /// Gets file paths from cache directory. If the files are broken, it will delete them.
    /// </summary>
    /// <param name="name">name of material</param>
    /// <returns></returns>
    private (string filename_b, string filename_bn, string filename_rm, string filename_d, string filename_n) GetMaterialFilePathsFromCache(
        string name, bool deleteAll = false)
    {
        var filename_b = Path.Combine(ISettingsManager.GetTemp_OBJPath(), name + ".png");
        CheckFile(filename_b);
        var filename_bn = Path.Combine(ISettingsManager.GetTemp_OBJPath(), name + "_n.png");
        CheckFile(filename_bn);
        var filename_rm = Path.Combine(ISettingsManager.GetTemp_OBJPath(), name + "_rm.png");
        CheckFile(filename_rm);
        var filename_d = Path.Combine(ISettingsManager.GetTemp_OBJPath(), name + "_d.dds");
        CheckFile(filename_d);
        var filename_n = Path.Combine(ISettingsManager.GetTemp_OBJPath(), name + "_n.dds");
        CheckFile(filename_n);

        return (filename_b, filename_bn, filename_rm, filename_d, filename_n);

        void CheckFile(string filePath)
        {
            var fileInfo = new FileInfo(filePath);
            if (fileInfo.Exists && (deleteAll || fileInfo.Length == 0))
            {
                try
                {
                    File.Delete(filePath);
                }
                catch
                {
                    _loggerService.Error($"Failed to delete {filePath}. To clear the material cache, try deleting it by hand.");
                }
            }
        }
    }

    public List<Material> GetMaterialsForAppearance(CMesh mesh, CName appearance)
    {
        var materials = GetMaterialsFromMesh(mesh);

        var appMaterials = new List<Material>();

        var amaterials = mesh.Appearances.FirstOrDefault(x => x is not null && x.Chunk.NotNull().Name == appearance, mesh.Appearances[0].NotNull()).NotNull().Chunk.NotNull().ChunkMaterials;
        foreach (var materialName in amaterials)
        {
            var name = GetUniqueMaterialName(materialName.ToString().NotNull(), mesh);
            appMaterials.Add(materials.ContainsKey(name) ? materials[name] : new Material(name));
        }

        return appMaterials;
    }

    private string GetUniqueMaterialName(string name, CMesh mesh) => mesh.InplaceResources.Count > 0
        ? Path.GetFileNameWithoutExtension(mesh.InplaceResources[0].DepotPath.GetResolvedText().NotNull()) 
        : name;

    private Dictionary<string, Material> GetMaterialsFromMesh(CMesh mesh)
    {
        var materials = new Dictionary<string, Material>();

        var localList = mesh.LocalMaterialBuffer.RawData?.Buffer.Data as CR2WList ?? null;


        foreach (var me in mesh.MaterialEntries)
        {
            var name = GetUniqueMaterialName(me.Name.ToString().NotNull(), mesh);
            if (!me.IsLocalInstance)
            {
                if (!materials.ContainsKey(me.Name.ToString().NotNull()))
                {
                    materials.Add(me.Name.ToString().NotNull(), new Material(name));
                }
                continue;
            }
            
            var inst = localList != null && localList.Files.Count > me.Index
                ? (CMaterialInstance)localList.Files[me.Index].RootChunk
                : (CMaterialInstance)mesh.PreloadLocalMaterialInstances[me.Index].NotNull().GetValue().NotNull();

            if (inst == null)
            {
                
                Parent.GetLoggerService().Warning($"Couldn't find material instance for index {me.Index}");
                continue;
            }

            var material = new Material(name)
            {
                Name = name
            };

            foreach (var pair in inst.Values)
            {
                var k = pair.Key.ToString().NotNull();

                if (!material.Values.ContainsKey(k))
                {
                    material.Values.Add(k, pair.Value);
                }
            }

            if (!materials.ContainsKey(name))
            {
                materials.Add(name, material);
            }
        }

        return materials;
    }

    [RelayCommand]
    public void CopyAppearanceName()
    {
        if (SelectedAppearance?.Name is not null)
        {
            Clipboard.SetText(SelectedAppearance.Name);
        }
    }

    public bool IsAppearanceSelected => null != SelectedAppearance;

    public bool IsLoadingMaterials { get; set; }

    private void DeleteMaterialCache()
    {
        if (!Directory.Exists(ISettingsManager.GetTemp_OBJPath()))
        {
            return;
        }

        try
        {
            var files = Directory.GetFiles(ISettingsManager.GetTemp_OBJPath());
            foreach (var file in files)
            {
                File.Delete(file);
            }
        }
        catch
        {
            // Don't delete, then
        }
    }

    [RelayCommand]
    public void LoadMaterials()
    {
        if (SelectedAppearance == null)
        {
            Parent.GetLoggerService().NotNull().Warning($"No material selected!");
            return;
        }
        IsLoadingMaterials = true;
        if (CtrlKeyPressed)
        {
            DeleteMaterialCache();
            
            Parent.GetLoggerService().NotNull().Info($"Clearing material cache...");
            foreach (var (_, material) in SelectedAppearance.RawMaterials)
            {
                ClearMaterial(material);
            }

            if (ShiftKeyPressed)
            {
                Parent.GetLoggerService().NotNull().Info($"All materials cleared!");
                return;
            }
        }

        
        Parallel.ForEachAsync(from entry in SelectedAppearance.RawMaterials orderby entry.Key ascending select entry, (material, cancellationToken) => LoadMaterial(material.Value)).ContinueWith((result) =>
        {
            Parent.GetLoggerService().NotNull().Info($"All materials loaded!");
            IsLoadingMaterials = false;
        });


        //IsLoadingMaterials = true;
        //var tasks = new List<Task>();

        //foreach (var (name, material) in RawMaterials)
        //{
        //    tasks.Add(LoadMaterial(material));
        //}

        //Task.WhenAll(tasks).ContinueWith((result) =>
        //{
        //    IsLoadingMaterials = false;
        //});
    }

    private void ClearMaterial(Material? material)
    {
        if (material == null)
        {
            return;
        }

        GetMaterialFilePathsFromCache(material.Name, true);
    }

    public async ValueTask LoadMaterial(WolvenKit.App.Models.Material? material)
    {
        if (material == null)
        {
            return;
        }

        Parent.GetLoggerService().Info($"Loading material: {material.Name}");

        var dictionary = material.Values;

        var mat = material.Instance;
        while (mat != null && mat.BaseMaterial.DepotPath != ResourcePath.Empty)
        {
            CR2WFile? baseMaterialFile = null;

            try
            {
                baseMaterialFile = Parent.GetFileFromDepotPathOrCache(mat.BaseMaterial.DepotPath);
            }
            catch
            {
                Parent.GetLoggerService().Warning($"Trying to find base material, but was not found: \n{mat.BaseMaterial.DepotPath}");
                continue;
            }

            if (baseMaterialFile == null)
            {
                mat = null;
                continue;
            }


            switch (baseMaterialFile.RootChunk)
            {
                case CMaterialInstance cmi:
                {
                    foreach (var pair in cmi.Values)
                    {
                        var k = pair.Key.ToString().NotNull();

                        if (!dictionary.ContainsKey(k))
                        {
                            dictionary.Add(k, pair.Value);
                        }
                    }

                    mat = cmi;
                    break;
                }
                case CMaterialTemplate cmt:
                    material.TemplateName = cmt.Name;
                    mat = null;
                    break;
                default:
                    break;
            }

        }

        // set numeric roughness, metalness etc. values from textures
        adjustRoughness(dictionary, material);

        var (filename_b, filename_bn, filename_rm, filename_d, filename_n) = GetMaterialFilePathsFromCache(material.Name);

        if (dictionary.TryGetValue("MultilayerSetup", out var mlsetup) && dictionary.TryGetValue("MultilayerMask", out var mlmask))
        {
            var albedoExists = File.Exists(filename_b);
            var roughMetallicExists = File.Exists(filename_rm);
            var normalExists = File.Exists(filename_bn);

            if (albedoExists && normalExists && roughMetallicExists)
            {
                goto DiffuseMaps;
            }

            if (mlsetup is not CResourceReference<Multilayer_Setup> mlsRef)
            {
                goto DiffuseMaps;
            }

            if (mlmask is not CResourceReference<Multilayer_Mask> mlmRef)
            {
                goto DiffuseMaps;
            }

            var setupFile = Parent.GetFileFromDepotPathOrCache(mlsRef.DepotPath);

            if (setupFile is not { RootChunk: Multilayer_Setup mls })
            {
                goto DiffuseMaps;
            }

            var maskFile = Parent.GetFileFromDepotPathOrCache(mlmRef.DepotPath);

            if (maskFile is not { RootChunk: Multilayer_Mask mlm })
            {
                goto DiffuseMaps;
            }

            ModTools.ConvertMultilayerMaskToDdsStreams(mlm, out var streams);


            var firstStream = await ImageDecoder.RenderToBitmapImageDds(streams[0], Enums.ETextureRawFormat.TRF_Grayscale);
            if (firstStream == null)
            {
                _loggerService.Error("Could not load MultilayerMask");
                return;
            }

            var destBitmap = new Bitmap((int)firstStream.PixelWidth, (int)firstStream.PixelHeight);
            var rmBitmap = new Bitmap((int)firstStream.PixelWidth, (int)firstStream.PixelHeight);
            var normalBitmap = new Bitmap((int)firstStream.PixelWidth, (int)firstStream.PixelHeight);
            using (var gfx_n = Graphics.FromImage(normalBitmap))
            using (var brush = new SolidBrush(System.Drawing.Color.FromArgb(128, 128, 255)))
            {
                gfx_n.FillRectangle(brush, 0, 0, (int)firstStream.PixelWidth, (int)firstStream.PixelHeight);
            }

            var gfx = Graphics.FromImage(destBitmap);
            var gfx_rm = Graphics.FromImage(rmBitmap);
            //Graphics gfx_n = Graphics.FromImage(destBitmap);

            var i = 0;
            foreach (var layer in mls.Layers)
            {
                if (i >= streams.Count)
                {
                    break;
                }

                if (layer.Material.DepotPath == ResourcePath.Empty)
                {
                    goto SkipLayer;
                }

                var templateFile = Parent.GetFileFromDepotPathOrCache(layer.Material.DepotPath);

                if (templateFile?.RootChunk is not Multilayer_LayerTemplate mllt)
                {
                    goto SkipLayer;
                }

                var tmp = i == 0 ? firstStream : await ImageDecoder.RenderToBitmapImageDds(streams[i], Enums.ETextureRawFormat.TRF_Grayscale);
                if (tmp == null)
                {
                    _loggerService.Error("Could not load Multilayer_Layer");
                    continue;
                }

                var mask = new TransformedBitmap(tmp, new ScaleTransform(1, 1));

                Bitmap maskBitmap;
                using (var outStream = new MemoryStream())
                {
                    BitmapEncoder enc = new PngBitmapEncoder();
                    enc.Frames.Add(BitmapFrame.Create(mask));
                    enc.Save(outStream);
                    maskBitmap = new Bitmap(outStream);
                }

                if (layer.ColorScale == "null_null" || layer.Opacity == 0 || layer.Material.DepotPath == ResourcePath.Empty)
                {
                    goto SkipColor;
                }

                {
                    var color = mllt.Overrides.ColorScale.Where(x => x is not null && x.N == layer.ColorScale).FirstOrDefault()?.V ?? null;

                    if (color == null)
                    {
                        goto SkipColor;
                    }

                    var colorMatrix = new ColorMatrix(new float[][]
                    {
                        new float[] { 0, 0, 0, 0, 0},
                        new float[] { 0, 0, 0, 0, 0},
                        new float[] { 0, 0, 0, 0, 0},
                        new float[] { 0, 0, 0, 0, 0},
                        new float[] { 0, 0, 0, 0, 0},
                    })
                    {
                        Matrix03 = layer.Opacity,
                        Matrix40 = color[0],
                        Matrix41 = color[1],
                        Matrix42 = color[2]
                    };

                    var attributes = new ImageAttributes();

                    attributes.SetColorMatrix(colorMatrix, ColorMatrixFlag.Default, ColorAdjustType.Bitmap);

                    gfx.DrawImage(maskBitmap, new Rectangle(0, 0, maskBitmap.Width, maskBitmap.Height), 0, 0, maskBitmap.Width, maskBitmap.Height, GraphicsUnit.Pixel, attributes);
                }
        
            
            #region skipColor
            SkipColor:

                {
                    var roughOut = mllt.Overrides.RoughLevelsOut.Where(x => x is not null && x.N == layer.RoughLevelsOut).FirstOrDefault()?.V ?? null;

                    var metalOut = mllt.Overrides.MetalLevelsOut.Where(x => x is not null && x.N == layer.MetalLevelsOut).FirstOrDefault()?.V ?? null;

                    var colorMatrix = new ColorMatrix(new float[][]
                    {
                        new float[] { 0, 0, 0, 0, 0},
                        new float[] { 0, 0, 0, 0, 0},
                        new float[] { 0, 0, 0, 0, 0},
                        new float[] { 0, 0, 0, 0, 0},
                        new float[] { 0, 0, 0, 0, 0},
                    })
                    {
                        Matrix03 = 1f,
                        Matrix40 = 0,
                        Matrix41 = roughOut != null ? (float)((roughOut[0] + roughOut[1]) / 2f) : 0.5f,
                        Matrix42 = metalOut != null ? (float)((metalOut[0] + metalOut[1]) / 2f) : 0.0f
                    };

                    var attributes = new ImageAttributes();

                    attributes.SetColorMatrix(colorMatrix, ColorMatrixFlag.Default, ColorAdjustType.Bitmap);

                    gfx_rm.DrawImage(maskBitmap, new Rectangle(0, 0, maskBitmap.Width, maskBitmap.Height), 0, 0, maskBitmap.Width, maskBitmap.Height, GraphicsUnit.Pixel, attributes);
                }

                //SkipRoughMetal:

                var normalFile = Parent.GetFileFromDepotPathOrCache(mllt.NormalTexture.DepotPath);

                if (normalFile != null && normalFile.RootChunk is ITexture it)
                {
                    var stream = new MemoryStream();
                    ModTools.ConvertRedClassToDdsStream(it, stream, out _, out var decompressedFormat, true);

                    var normal = await ImageDecoder.RenderToBitmapImageDds(stream, decompressedFormat);
                    if (normal == null)
                    {
                        _loggerService.Error($"Could not load NormalTexture \"{mllt.NormalTexture.DepotPath}\"");
                        goto SkipNormals;
                    }

                    Bitmap normalLayer;
                    using (var outStream = new MemoryStream())
                    {
                        BitmapEncoder enc = new PngBitmapEncoder();
                        enc.Frames.Add(BitmapFrame.Create(normal));
                        enc.Save(outStream);
                        normalLayer = new Bitmap(outStream);
                    }

                    var layerWidth = (int)(normalLayer.Width * layer.MatTile);
                    var layerHeight = (int)(normalLayer.Height * layer.MatTile);

                    var tempNormalBitmap = new Bitmap(maskBitmap.Width, maskBitmap.Height);

                    if (layerWidth != 0 && layerHeight != 0)
                    {
                        try
                        {
                            tempNormalBitmap = new Bitmap(layerWidth < maskBitmap.Width ? layerWidth : maskBitmap.Width, layerHeight < maskBitmap.Height ? layerHeight : maskBitmap.Height);
                        }
                        catch (Exception)
                        {
                            ;
                        }
                    }

                    var gfx_n = Graphics.FromImage(tempNormalBitmap);
                    gfx_n.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBilinear;
                    gfx_n.DrawImage(normalLayer, new Rectangle(0, 0, layerWidth, layerHeight), 0, 0, normalLayer.Width, normalLayer.Height, GraphicsUnit.Pixel, null);
                    gfx_n.Dispose();

                    foreach (var strength in mllt.Overrides.NormalStrength)
                    {
                        if (strength.N == layer.NormalStrength)
                        {
                            for (var y = 0; y < maskBitmap.Height; y++)
                            {
                                for (var x = 0; x < maskBitmap.Width; x++)
                                {
                                    var oc = normalBitmap.GetPixel(x, y);
                                    var n = tempNormalBitmap.GetPixel(x % tempNormalBitmap.Width, y % tempNormalBitmap.Height);
                                    var alpha = maskBitmap.GetPixel(x, y).R / 255F * (float)strength.V;
                                    var r = (int)((oc.R - 127) * (1F - alpha) + (n.R - 127) * alpha) + 127;
                                    //var g = 255 - ((int)((oc.G - 127) * (1F - alpha) + (n.G - 127) * alpha) + 127);
                                    var g = (int)((oc.G - 127) * (1F - alpha) + (n.G - 127) * alpha) + 127;
                                    var b = (int)((oc.B - 127) * (1F - alpha)) + 127;
                                    if (n.B == 0)
                                    {
                                        b += (int)((ToBlue((byte)r, (byte)g) - 127) * alpha);
                                    }
                                    else
                                    {
                                        b += (int)((n.B - 127) * alpha);
                                    }
                                    var color = System.Drawing.Color.FromArgb(Math.Clamp(r, 0, 255), Math.Clamp(g, 0, 255), Math.Clamp(b, 0, 255));
                                    normalBitmap.SetPixel(x, y, color);
                                }
                            }
                            break;
                        }
                    }
                    stream.Dispose();
                }

                SkipLayer:
                i++;
            }

            gfx.Dispose();
            //gfx_n.Dispose();

            try
            {
                destBitmap.Save(filename_b, ImageFormat.Png);
            }
            catch (Exception e)
            {
                Parent.GetLoggerService().Error(e.Message);
            }
            finally
            {
                destBitmap.Dispose();
            }

            try
            {
                rmBitmap.Save(filename_rm, ImageFormat.Png);
            }
            catch (Exception e)
            {
                Parent.GetLoggerService().Error(e.Message);
            }
            finally
            {
                rmBitmap.Dispose();
            }

            try
            {
                normalBitmap.Save(filename_bn, ImageFormat.Png);
            }
            catch (Exception e)
            {
                Parent.GetLoggerService().Error(e.Message);
            }
            finally
            {
                normalBitmap.Dispose();
            }
        }
    
        #endregion
        #region diffuseMaps
    DiffuseMaps:
        if (File.Exists(filename_d))
        {
            goto NormalMaps;
        }

        if (dictionary.TryGetValue("DiffuseTexture", out var diffuse) && diffuse is CResourceReference<ITexture> crrd)
        {
            var xbm = Parent.GetFileFromDepotPathOrCache(crrd.DepotPath);

            if (xbm?.RootChunk is not ITexture it)
            {
                goto NormalMaps;
            }

            var stream = new FileStream(filename_d, FileMode.Create);
            ModTools.ConvertRedClassToDdsStream(it, stream, out var format, out _, true);
            stream.Dispose();
        }

        if (dictionary.TryGetValue("ParalaxTexture", out var paralax) && paralax is CResourceReference<ITexture> crrp)
        {
            var xbm = Parent.GetFileFromDepotPathOrCache(crrp.DepotPath);

            if (xbm?.RootChunk is not ITexture it)
            {
                goto NormalMaps;
            }

            var stream = new FileStream(filename_d, FileMode.Create);
            ModTools.ConvertRedClassToDdsStream(it, stream, out var format, out _, true);
            stream.Dispose();
        }

        if (dictionary.TryGetValue("BaseColor", out var baseColorTex) && baseColorTex is CResourceReference<ITexture> crrbc)
        {
            var xbm = Parent.GetFileFromDepotPathOrCache(crrbc.DepotPath);

            if (xbm == null || xbm.RootChunk is not ITexture it)
            {
                goto NormalMaps;
            }

            var stream = new FileStream(filename_d, FileMode.Create);
            ModTools.ConvertRedClassToDdsStream(it, stream, out var format, out _, true);
            stream.Dispose();
        }

        #endregion
        
        #region normalMaps
    NormalMaps:

        // normals

        if (File.Exists(filename_bn))
        {
            goto SkipNormals;
        }

        if (dictionary.TryGetValue("NormalTexture", out var normalTex) && normalTex is CResourceReference<ITexture> crrn)
        {
            var xbm = Parent.GetFileFromDepotPathOrCache(crrn.DepotPath);

            if (xbm?.RootChunk is not ITexture it)
            {
                goto SkipNormals;
            }

            //var stream = new FileStream(filename_n, FileMode.Create);
            //ModTools.ConvertRedClassToDdsStream(it, stream, out var format);
            //stream.Dispose();

            var stream = new MemoryStream();
            ModTools.ConvertRedClassToDdsStream(it, stream, out _, out var decompressedFormat, true);

            var normal = await ImageDecoder.RenderToBitmapImageDds(stream, decompressedFormat);
            if (normal == null)
            {
                _loggerService.Error($"Could not load NormalTexture \"{crrn.DepotPath}\"");
                goto SkipNormals;
            }

            stream.Dispose();

            Bitmap normalLayer;
            using (var outStream = new MemoryStream())
            {
                BitmapEncoder enc = new PngBitmapEncoder();
                enc.Frames.Add(BitmapFrame.Create(normal));
                enc.Save(outStream);
                normalLayer = new Bitmap(outStream);
            }

            for (var y = 0; y < normalLayer.Height; y++)
            {
                for (var x = 0; x < normalLayer.Width; x++)
                {
                    var oc = normalLayer.GetPixel(x, y);
                    var r = oc.R;
                    //var g = (byte)(255 - oc.G);
                    var g = oc.G;
                    var b = ToBlue(r, g);
                    normalLayer.SetPixel(x, y, System.Drawing.Color.FromArgb(r, g, b));
                }
            }

            try
            {
                normalLayer.Save(filename_bn, ImageFormat.Png);
            }
            catch (Exception e)
            {
                Parent.GetLoggerService().Error(e.Message);
            }
            finally
            {
                normalLayer.Dispose();
            }

        }
        else if (dictionary.TryGetValue("Normal", out var normalTex2) && normalTex2 is CResourceReference<ITexture> crrn2)
        {
            var xbm = Parent.GetFileFromDepotPathOrCache(crrn2.DepotPath);

            if (xbm?.RootChunk is not ITexture it)
            {
                goto SkipNormals;
            }

            //var stream = new FileStream(filename_n, FileMode.Create);
            //ModTools.ConvertRedClassToDdsStream(it, stream, out var format);
            //stream.Dispose();

            var stream = new MemoryStream();
            ModTools.ConvertRedClassToDdsStream(it, stream, out _, out var decompressedFormat, true);

            var normal = await ImageDecoder.RenderToBitmapImageDds(stream, decompressedFormat);
            if (normal == null)
            {
                _loggerService.Error($"Could not load NormalTexture \"{crrn2.DepotPath}\"");
                goto SkipNormals;
            }

            stream.Dispose();

            Bitmap normalLayer;
            using (var outStream = new MemoryStream())
            {
                BitmapEncoder enc = new PngBitmapEncoder();
                enc.Frames.Add(BitmapFrame.Create(normal));
                enc.Save(outStream);
                normalLayer = new Bitmap(outStream);
            }

            for (var y = 0; y < normalLayer.Height; y++)
            {
                for (var x = 0; x < normalLayer.Width; x++)
                {
                    var oc = normalLayer.GetPixel(x, y);
                    var r = oc.R;
                    //var g = (byte)(255 - oc.G);
                    var g = oc.G;
                    var b = ToBlue(r, g);
                    normalLayer.SetPixel(x, y, System.Drawing.Color.FromArgb(r, g, b));
                }
            }

            try
            {
                normalLayer.Save(filename_bn, ImageFormat.Png);
            }
            catch (Exception e)
            {
                Parent.GetLoggerService().Error(e.Message);
            }
            finally
            {
                normalLayer.Dispose();
            }
        }

        #endregion
        
        #region skipNormals
    SkipNormals:
        DispatcherHelper.RunOnMainThread(() => SetupPBRMaterial(material.Name, true));

        #endregion
        return;
    }

    /** Set default roughness values for material */
    private static void adjustRoughness(Dictionary<string, object> dictionary, Material material)
    {
        material.Values = dictionary;

        if (dictionary.TryGetValue("Metalness", out var metalness) && metalness is CResourceReference<ITexture> metalTexture)
        {
            if (metalTexture.DepotPath == @"base\materials\placeholder\black.xbm")
            {
                material.Metalness = 0;
            }
            else if (metalTexture.DepotPath == @"base\materials\placeholder\white.xbm")
            {
                material.Metalness = 1;
            }
        }

        if (dictionary.TryGetValue("MetalnessScale", out var metalnessScale))
        {
            material.Metalness = (CFloat)metalnessScale;
        }

        if (dictionary.TryGetValue("Roughness", out var roughness) && roughness is CResourceReference<ITexture> roughTexture)
        {
            if (roughTexture.DepotPath == @"base\materials\placeholder\black.xbm")
            {
                material.Roughness = 0;
            }
            else if (roughTexture.DepotPath == @"base\materials\placeholder\white.xbm")
            {
                material.Roughness = 1;
            }
        }

        if (dictionary.TryGetValue("RoughnessScale", out var roughnessScale))
        {
            material.Roughness = (CFloat)roughnessScale;
        }
    }

    public byte ToBlue(byte r, byte g) => (byte)Math.Clamp(Math.Round((Math.Sqrt(1.02 - 2 * (r / 255F * 2 - 1) * (g / 255F * 2 - 1)) + 1) / 2 * 255), 0, 255);


    #endregion

    #region meshpreviewsector

    public partial class MeshComponentSelector : ObservableObject
    {
        private const string s_noSelection = "No_Selection";

        public string Name
        {
            get => _name;
            private set => SetProperty(ref _name, value);
        }

        public string WorldNodeIndex
        {
            get => _worldNodeIndex;
            private set => SetProperty(ref _worldNodeIndex, value);
        }

        public bool IsValid
        {
            get => _isValid;
            private set => SetProperty(ref _isValid, value);
        }

        private MeshComponent? _meshComponent;
        private string _name = s_noSelection;
        private string _worldNodeIndex = string.Empty;
        private bool _isValid;

        public MeshComponent? SelectedMesh
        {
            get => _meshComponent;
            set
            {
                _meshComponent = value;
                Name = value == null ? s_noSelection : value.Name.StartsWith("_") ? value.Name[1..] : value.Name;
                WorldNodeIndex = value == null ? string.Empty : "[" + value.WorldNodeIndex + "]";
                IsValid = value != null;
            }
        }

        public void Unselect() => SelectedMesh = null;
    }

    [ObservableProperty] private MeshComponentSelector _currentSelection = new();

   

    public void RenderSectorSolo()
    {
        RetoreSelectedMeshMaterial();
        CurrentSelection = new();
        if (IsRendered)
        {
            return;
        }
        IsRendered = true;
        if (_data is worldStreamingSector wss)
        {
            RenderSector(wss, Appearances[0]);
        }
    }

    public SectorGroup RenderSector(worldStreamingSector data, Appearance app)
    {
        if ( data.NodeData.Data is not worldNodeDataBuffer buffer)
        {
            throw new ArgumentNullException();
        }

        var ssTransforms = buffer.Lookup;

        var groups = new List<Element3D>();
        foreach (var (handleIndex, transforms) in ssTransforms)
        {
            var handle = data.Nodes[handleIndex].NotNull();
            var name = handle.Chunk.NotNull().DebugName.ToString().NotNull();
            name = "_" + name.Replace(" ", "_").Replace("[", "").Replace("]", "").Replace("{", "").Replace("}", "").Replace("\\", "_").Replace(".", "_").Replace("!", "").Replace("-", "_") ?? "none";

            var indexStr = string.Join(", ", transforms.Select(x => buffer.IndexOf(x)));
            var typeStr = handle.Chunk.GetType().Name;

            if (handle.Chunk is IRedMeshNode irmn)
            {
                var meshFile = Parent.GetFileFromDepotPathOrCache(irmn.Mesh.DepotPath);

                if (meshFile == null || meshFile.RootChunk is not CMesh mesh)
                {
                    continue;
                }

                var appMaterials = GetMaterialsForAppearance(mesh, irmn.MeshAppearance);

                var group = new MeshComponent()
                {
                    Name = name,
                    Text = name,
                    WorldNodeIndex = $"{handleIndex}",
                    WorldNodeType = typeStr,
                    WorldNodeDataIndices = indexStr,
                    AppearanceName = irmn.MeshAppearance,
                    DepotPath = irmn.Mesh.DepotPath
                };

                if (group.WorldNodeIndex == SelectedNodeIndex)
                {
                    SelectedItem = group;
                }

                var model = new LoadableModel(name)
                {
                    MeshFile = Parent.Cr2wFile,
                    AppearanceIndex = 0,
                    AppearanceName = irmn.MeshAppearance,
                    Materials = appMaterials,
                    IsEnabled = true,
                };

                foreach (var material in model.Materials)
                {
                    app.RawMaterials[material.Name] = material;
                }

                if (handle.Chunk is worldInstancedMeshNode wimn)
                {
                    if (wimn.WorldTransformsBuffer.SharedDataBuffer.Chunk.NotNull().Buffer.Data is not WorldTransformsBuffer wtb)
                    {
                        continue;
                    }

                    var wtbTransforms = wtb.Transforms;
                    for (var i = 0; i < wimn.WorldTransformsBuffer.NumElements; i++)
                    {
                        var meshes = MakeMesh(mesh, ulong.MaxValue, 0);

                        var subgroup = new MeshComponent()
                        {
                            Name = name + $"_instance_{i:D2}",
                            Text = name + $"_instance_{i:D2}",
                            AppearanceName = wimn.MeshAppearance,
                            DepotPath = irmn.Mesh.DepotPath
                        };

                        foreach (var submesh in meshes)
                        {
                            if (!app.LODLUT.ContainsKey(submesh.LOD))
                            {
                                app.LODLUT[submesh.LOD] = new List<SubmeshComponent>();
                            }
                            app.LODLUT[submesh.LOD].Add(submesh);
                            subgroup.Children.Add(submesh);
                        }

                        var matrix = new Matrix3D();
                        matrix.Scale(ToScaleVector3D(transforms[0].Scale));
                        matrix.Rotate(ToQuaternion(transforms[0].Orientation));
                        matrix.Translate(ToVector3D(transforms[0].Position));

                        var wtbMatrix = new Matrix3D();
                        if (wtbTransforms[(int)(wimn.WorldTransformsBuffer.StartIndex + i)] is worldNodeTransform wte)
                        {
                            wtbMatrix.Scale(ToScaleVector3D(wte.Scale));
                        }

                        var transform = wtbTransforms[(int)(wimn.WorldTransformsBuffer.StartIndex + i)].NotNull();
                        wtbMatrix.Rotate(ToQuaternion(transform.Rotation));
                        wtbMatrix.Translate(ToVector3D(transform.Translation));

                        wtbMatrix.Append(matrix);

                        subgroup.Transform = new MatrixTransform3D(wtbMatrix);

                        group.Children.Add(subgroup);
                    }
                }
                else if (handle.Chunk is worldInstancedDestructibleMeshNode widmn)
                {
                    if (widmn.CookedInstanceTransforms.SharedDataBuffer?.Chunk.NotNull().Buffer.Data is not CookedInstanceTransformsBuffer citb)
                    {
                        continue;
                    }

                    var citbTransforms = citb.Transforms;
                    for (var i = 0; i < widmn.CookedInstanceTransforms.NumElements; i++)
                    {
                        //for (int j = 0; j < transforms.Count; j++)
                        //{
                        var meshes = MakeMesh(mesh, ulong.MaxValue, 0);

                        var subgroup = new MeshComponent()
                        {
                            Name = name + $"_instance_{i:D2}",
                            Text = name + $"_instance_{i:D2}",
                            AppearanceName = widmn.MeshAppearance,
                            DepotPath = irmn.Mesh.DepotPath
                        };

                        foreach (var submesh in meshes)
                        {
                            if (!app.LODLUT.ContainsKey(submesh.LOD))
                            {
                                app.LODLUT[submesh.LOD] = new List<SubmeshComponent>();
                            }
                            app.LODLUT[submesh.LOD].Add(submesh);
                            subgroup.Children.Add(submesh);
                        }

                        var matrix = new Matrix3D();
                        matrix.Scale(ToScaleVector3D(transforms[0].Scale));
                        matrix.Rotate(ToQuaternion(transforms[0].Orientation));
                        matrix.Translate(ToVector3D(transforms[0].Position));

                        var citbMatrix = new Matrix3D();
                        var transform = citbTransforms[(int)(widmn.CookedInstanceTransforms.StartIndex + i)].NotNull();
                        citbMatrix.Rotate(ToQuaternion(transform.Orientation));
                        citbMatrix.Translate(ToVector3D(transform.Position));

                        citbMatrix.Append(matrix);

                        subgroup.Transform = new MatrixTransform3D(citbMatrix);

                        group.Children.Add(subgroup);
                        //}
                    }
                }
                else
                {
                    var i = 0;
                    foreach (var transform in transforms)
                    {
                        var meshes = MakeMesh(mesh, ulong.MaxValue, 0);

                        var subgroup = new MeshComponent()
                        {
                            Name = name + $"_instance_{i:D2}",
                            Text = name + $"_instance_{i:D2}",
                            AppearanceName = irmn.MeshAppearance,
                            DepotPath = irmn.Mesh.DepotPath
                        };

                        foreach (var submesh in meshes)
                        {
                            if (!app.LODLUT.ContainsKey(submesh.LOD))
                            {
                                app.LODLUT[submesh.LOD] = new List<SubmeshComponent>();
                            }
                            app.LODLUT[submesh.LOD].Add(submesh);
                            subgroup.Children.Add(submesh);
                        }

                        var matrix = new Matrix3D();
                        matrix.Scale(ToScaleVector3D(transform.Scale));
                        matrix.Rotate(ToQuaternion(transform.Orientation));
                        matrix.Translate(ToVector3D(transform.Position));

                        if (irmn is worldBendedMeshNode wbmn)
                        {
                            //matrix.Append(ToMatrix3D(wbmn.DeformationData[i]));
                        }

                        subgroup.Transform = new MatrixTransform3D(matrix);

                        group.Children.Add(subgroup);
                        i++;
                    }
                }

                groups.Add(group);
            }
            else if (handle.Chunk is worldCollisionNode wcn)
            {
                if (wcn.CompiledData.Data is not CollisionBuffer cb)
                {
                    continue;
                }

                var mesh = new MeshComponent()
                {
                    WorldNodeIndex = $"{handleIndex}",
                    WorldNodeType = typeStr,
                    WorldNodeDataIndices = indexStr,
                    Name = "collisionNode_" + wcn.SectorHash,
                    Text = "collisionNode_" + wcn.SectorHash
                };

                if (mesh.WorldNodeIndex == SelectedNodeIndex)
                {
                    SelectedItem = mesh;
                }
                
                var colliderMaterial = new PBRMaterial()
                {
                    EnableAutoTangent = true,
                    RenderShadowMap = true,
                    RenderEnvironmentMap = true,
                    //AlbedoColor = new SharpDX.Color4(0.5f * Random.Shared.NextSingle(), 0f, 0.5f * Random.Shared.NextSingle(), 1f),
                    AlbedoColor = new SharpDX.Color4(0.5f, 0f, 0.5f, 1f),
                    RoughnessFactor = 0.5,
                    MetallicFactor = 0
                };

                for (var k = 0; k < cb.Actors.Count; k++)
                {
                    var actor = cb.Actors[k];
                    
                    var actorGroup = new MeshComponent()
                    {
                        WorldNodeIndex = $"{handleIndex}",
                        WorldNodeType = typeStr,
                        WorldNodeDataIndices = indexStr,
                        Name = "actor_" + cb.Actors.IndexOf(actor),
                        Text = "actor_" + cb.Actors.IndexOf(actor),
                        CollisionActorId = $"{k}"
                    };

                    if (mesh.WorldNodeIndex == SelectedNodeIndex)
                    {
                        SelectedItem = mesh;
                    }
                    
                    foreach (var shape in actor.Shapes)
                    {
                        HelixToolkit.SharpDX.Core.MeshGeometry3D? geometry = null;

                        if (shape is CollisionShapeSimple simpleShape && simpleShape.ShapeType == Enums.physicsShapeType.Box)
                        {
                            var mb = new MeshBuilder
                            {
                                CreateNormals = true
                            };
                            mb.AddBox(new SharpDX.Vector3(0f, 0f, 0f), simpleShape.Size.X * 2, simpleShape.Size.Y * 2, simpleShape.Size.Z * 2);

                            mb.ComputeNormalsAndTangents(MeshFaces.Default, true);

                            geometry = mb.ToMeshGeometry3D();
                        }
                        else if (shape is CollisionShapeMesh meshShape)
                        {
                            var geo = _geometryCacheService.GetEntry(wcn.SectorHash, meshShape.Hash);

                            if (geo == null)
                            {
                                //_loggerService.Warning($"Couldn't find entry with hash {shape.Hash} in sector {wcn.SectorHash}, handle[{data.Handles.IndexOf(handle)}], actor[{cb.Actors.IndexOf(actor)}], shape[{actor.Shapes.IndexOf(shape)}]");
                                continue;
                            }

                            if (geo is ConvexMesh convexMesh)
                            {
                                var mb = new MeshBuilder
                                {
                                    CreateNormals = true
                                };

                                var positions = new Vector3Collection();
                                for (var i = 0; i < convexMesh.HullData.HullVertices.Count; i++)
                                {
                                    positions.Add(convexMesh.HullData.HullVertices[i]);
                                }

                                var faceIndex = 0;
                                for (var i = 0; i < convexMesh.HullData.Polygons.Count; i++)
                                {
                                    var count = mb.Positions.Count;
                                    var faceData = convexMesh.HullData.Polygons[i];

                                    switch (faceData.NbVerts)
                                    {
                                        case 3:
                                            mb.Positions.Add(positions[convexMesh.HullData.VertexData8[faceIndex]]);
                                            mb.Positions.Add(positions[convexMesh.HullData.VertexData8[faceIndex + 1]]);
                                            mb.Positions.Add(positions[convexMesh.HullData.VertexData8[faceIndex + 2]]);
                                            mb.Normals.Add(faceData.Plane.N);
                                            mb.Normals.Add(faceData.Plane.N);
                                            mb.Normals.Add(faceData.Plane.N);
                                            mb.TriangleIndices.Add(count);
                                            mb.TriangleIndices.Add(count + 1);
                                            mb.TriangleIndices.Add(count + 2);
                                            break;
                                        case 4:
                                            mb.Positions.Add(positions[convexMesh.HullData.VertexData8[faceIndex]]);
                                            mb.Positions.Add(positions[convexMesh.HullData.VertexData8[faceIndex + 1]]);
                                            mb.Positions.Add(positions[convexMesh.HullData.VertexData8[faceIndex + 2]]);
                                            mb.Positions.Add(positions[convexMesh.HullData.VertexData8[faceIndex + 3]]);
                                            mb.Normals.Add(faceData.Plane.N);
                                            mb.Normals.Add(faceData.Plane.N);
                                            mb.Normals.Add(faceData.Plane.N);
                                            mb.Normals.Add(faceData.Plane.N);
                                            mb.TriangleIndices.Add(count);
                                            mb.TriangleIndices.Add(count + 1);
                                            mb.TriangleIndices.Add(count + 2);
                                            mb.TriangleIndices.Add(count + 2);
                                            mb.TriangleIndices.Add(count + 3);
                                            mb.TriangleIndices.Add(count);
                                            break;
                                        default:
                                            for (var j = 0; j < faceData.NbVerts; j++)
                                            {
                                                mb.Positions.Add(positions[convexMesh.HullData.VertexData8[j]]);
                                                mb.Normals.Add(faceData.Plane.N);
                                            }
                                            for (var j = 0; j + 2 < faceData.NbVerts; j++)
                                            {
                                                mb.TriangleIndices.Add(count);
                                                mb.TriangleIndices.Add(count + j + 1);
                                                mb.TriangleIndices.Add(count + j + 2);
                                            }
                                            break;
                                    }

                                    faceIndex += faceData.NbVerts;
                                }

                                geometry = mb.ToMeshGeometry3D();
                            }

                            if (geo is BV4TriangleMesh bv4TriangleMesh)
                            {
                                var mb = new MeshBuilder
                                {
                                    CreateNormals = true
                                };

                                var positions = new Vector3Collection();
                                for (var i = 0; i < bv4TriangleMesh.Vertices.Count; i++)
                                {
                                    positions.Add(bv4TriangleMesh.Vertices[i]);
                                }

                                foreach (var face in bv4TriangleMesh.Triangles)
                                {
                                    var points = new List<SharpDX.Vector3>();
                                    foreach (var point in face)
                                    {
                                        points.Add(positions[(int)point]);
                                    }
                                    mb.AddTriangle(points[0], points[1], points[2]);
                                }

                                //mb.ComputeNormalsAndTangents(MeshFaces.Default);
                                
                                geometry = mb.ToMeshGeometry3D();
                            }
                        }

                        if (geometry == null)
                        {
                            continue;
                        }

                        ulong hash = 0;
                        if (shape is CollisionShapeMesh meshShape2)
                        {
                            hash = meshShape2.Hash;
                        }

                        var shapeGroup = new SubmeshComponent()
                        {
                            Name = "shape_" + hash,
                            Text = "shape_" + hash,
                            IsRendering = true,
                            Geometry = geometry,
                            Material = colliderMaterial,
                            IsTransparent = true,
                            CollisionActorId = $"{k}"
                        };

                        var shapeMatrix = new Matrix3D();
                        
                        var shapeQuat = new System.Numerics.Quaternion(shape.Rotation.I, shape.Rotation.J, shape.Rotation.K, shape.Rotation.R);
                        var conversionQuat =
                            System.Numerics.Quaternion.CreateFromAxisAngle(System.Numerics.Vector3.UnitX, -MathF.PI / 2);
                        var adjustedQuat = conversionQuat * shapeQuat;
                        
                        shapeMatrix.Rotate(new System.Windows.Media.Media3D.Quaternion(adjustedQuat.X, adjustedQuat.Y, adjustedQuat.Z, adjustedQuat.W));
                        shapeMatrix.Translate(ToVector3D(shape.Position));

                        shapeGroup.Transform = new MatrixTransform3D(shapeMatrix);

                        actorGroup.Children.Add(shapeGroup);
                    }

                    var matrix = new Matrix3D();
                    matrix.Scale(ToScaleVector3D(actor.Scale));
                    matrix.Rotate(ToQuaternion(actor.Orientation));
                    matrix.Translate(ToVector3D(actor.Position));

                    actorGroup.Transform = new MatrixTransform3D(matrix);

                    mesh.Children.Add(actorGroup);
                }

                // not used?
                //var meshMatrix = new Matrix3D();
                //meshMatrix.Scale(ToScaleVector3D(transforms[0].Scale));
                //meshMatrix.Rotate(ToQuaternion(transforms[0].Orientation));
                //meshMatrix.Translate(ToVector3D(transforms[0].Position));

                //mesh.Transform = new MatrixTransform3D(meshMatrix);
                groups.Add(mesh);
            }
            else if (handle.Chunk is worldNavigationNode wnm)
            {
                var emb = Parent.Cr2wFile.EmbeddedFiles.FirstOrDefault(x => x.FileName == wnm.NavigationTileResource.DepotPath);
                if (emb == null)
                {
                    continue;
                }

                if (emb.Content is not worldNavigationTileResource wntr)
                {
                    continue;
                }

                foreach (var tile in wntr.TilesData)
                {
                    if (wntr.TileBuffers[(int)(uint)tile.BufferIndex].Data is not TilesBuffer tb)
                    {
                        continue;
                    }

                    var positions = new Vector3Collection();

                    foreach (var v in tb.Vertices)
                    {
                        positions.Add(new SharpDX.Vector3(v.X, v.Y, -v.Z));
                    }

                    var mb = new MeshBuilder();

                    foreach (var f in tb.FaceInfo)
                    {
                        if (f.NumIndices == 3)
                        {
                            mb.AddTriangle(positions[f.Indices[0]], positions[f.Indices[2]], positions[f.Indices[1]]);
                        }
                        else if (f.NumIndices == 2)
                        {
                            mb.AddPipe(positions[f.Indices[0]], positions[f.Indices[1]], 0, 0.1, 16);
                        }
                    }

                    mb.ComputeNormalsAndTangents(MeshFaces.Default);

                    var material = SetupPBRMaterial("DefaultMaterial");
                    material.AlbedoColor = new SharpDX.Color4(1f, 1f, 1f, 0.5f);
                    //material.AlbedoColor = new SharpDX.Color4(.5f, .5f, .5f, 1.0f);

                    var mesh = new MeshGeometryModel3D()
                    {
                        Geometry = mb.ToMeshGeometry3D(),
                        Material = material,
                        IsTransparent = true
                        //Transform = new TranslateTransform3D(tile.TileX * 100, 0, tile.TileY * 100)
                    };

                    var group = new MeshComponent()
                    {
                        Name = name, Text = name, WorldNodeIndex = string.Empty, WorldNodeDataIndices = string.Empty
                    };

                    group.Children.Add(mesh);

                    groups.Add(group);
                }
            }
            else if (handle.Chunk is worldEntityNode wen)
            {
                //a little slow for what we want to do
                // <3

                try
                {

                    var entFile = Parent.GetFileFromDepotPathOrCache(wen.EntityTemplate.DepotPath);

                    if (entFile != null && entFile.RootChunk is entEntityTemplate eet)
                    {
                        var entity = RenderEntity(eet, app);
                        if (entity != null)
                        {
                            entity.Name = "Entity";
                            entity.Text = "Entity";
                            //var f = Shell.ChunkViewModel.FixRotation;

                            var q = new System.Numerics.Quaternion()
                            {
                                W = transforms[0].Orientation.R,
                                X = transforms[0].Orientation.I,
                                Y = transforms[0].Orientation.J,
                                Z = transforms[0].Orientation.K
                            };

                            //q = Shell.ChunkViewModel.FixRotation(q);
                            var qq = new System.Windows.Media.Media3D.Quaternion(q.X, q.Y, q.Z, q.W);

                            var matrix = new Matrix3D();
                            matrix.Scale(ToScaleVector3D(transforms[0].Scale));
                            matrix.Rotate(qq);
                            matrix.Translate(ToVector3D(transforms[0].Position));

                            entity.Transform = new MatrixTransform3D(matrix);

                            groups.Add(entity);
                        }
                    }
                }
                catch (Exception ex) { Parent.GetLoggerService().Error(ex); }
            }
            else if (handle.Chunk is worldPopulationSpawnerNode wpsn)
            {
                //var record = _tweakDBService.GetRecord(wpsn.ObjectRecordId);

                //if (record is gamedataVehicle_Record vehicle)
                //{
                //    var entFile = File.GetFileFromDepotPathOrCache(vehicle.EntityTemplatePath.DepotPath);

                //    if (entFile != null && entFile.RootChunk is entEntityTemplate eet)
                //    {
                //        var entity = RenderEntity(eet, app);
                //        if (entity != null)
                //        {
                //            entity.Name = "Entity";

                //            var matrix = new Matrix3D();
                //            matrix.Scale(ToScaleVector3D(transforms[0].Scale));
                //            matrix.Rotate(ToQuaternion(transforms[0].Orientation));
                //            matrix.Translate(ToVector3D(transforms[0].Position));

                //            entity.Transform = new MatrixTransform3D(matrix);

                //            groups.Add(entity);
                //        }
                //    }
                //}
            }
            else if (handle.Chunk is worldAreaShapeNode wasn)
            {
                var shape = wasn.Outline.Chunk.NotNull();
                var mb = new MeshBuilder();

                var center = new SharpDX.Vector3();

                foreach (var point in shape.Points)
                {
                    center.X += point.X / shape.Points.Count;
                    center.Y += point.Z / shape.Points.Count;
                    center.Z += -point.Y / shape.Points.Count;
                }

                mb.AddBox(center, Math.Abs(shape.Points[0].NotNull().X) * 2, shape.Height, Math.Abs(shape.Points[0].NotNull().Y) * 2);
                mb.ComputeNormalsAndTangents(MeshFaces.Default);

                var material = SetupPBRMaterial("DefaultMaterial");
                material.AlbedoColor = new SharpDX.Color4(1f, 1f, 1f, 0.1f);

                var mesh = new MeshGeometryModel3D()
                {
                    Geometry = mb.ToMeshGeometry3D(),
                    Material = material,
                    IsTransparent = true
                };

                var matrix = new Matrix3D();
                matrix.Scale(ToScaleVector3D(transforms[0].Scale));
                matrix.Rotate(ToQuaternion(transforms[0].Orientation));
                matrix.Translate(ToVector3D(transforms[0].Position));

                var group = new MeshComponent()
                {
                    Name = name,
                    Text = name,
                    Transform = new MatrixTransform3D(matrix)
                };

                group.Children.Add(mesh);

                groups.Add(group);

            }
        }

        var element = new SectorGroup()
        {
            Name = "",
            Text = "",
        };
        foreach (var group in groups)
        {
            element.Children.Add(group);
        }
        app.ModelGroup.Add(element);

        SelectWorldNode();
        return element;
    }

    public static SharpDX.Vector3 ToVector3(Vector3 v) => new(v.X, v.Y, v.Z);

    public void UpdateSelection(MeshComponent mesh)
    {
        RetoreSelectedMeshMaterial();

        CurrentSelection.SelectedMesh = mesh;

        UpdateMeshSelectionColor(mesh);
    }

    private void RetoreSelectedMeshMaterial() => UpdateChildrenSubmesh(CurrentSelection.SelectedMesh, (submesh) => submesh.Material = submesh.OriginalMaterial);
    private void UpdateMeshSelectionColor(MeshComponent mesh)
    {
        UpdateChildrenSubmesh(mesh, (submesh) =>
        {
            submesh.OriginalMaterial = submesh.Material;
            submesh.Material = PhongMaterials.Blue;
        });
    }

    private void UpdateChildrenSubmesh(MeshComponent? mesh, Action<SubmeshComponent>? submeshUpdater)
    {
        if (mesh == null)
        {
            return;
        }

        foreach (var child in mesh.Children)
        {
            switch (child)
            {
                case MeshComponent childMesh:
                    UpdateChildrenSubmesh(childMesh, submeshUpdater);
                    break;
                case SubmeshComponent childSubMesh:
                {
                    if (submeshUpdater != null)
                    {
                        submeshUpdater(childSubMesh);
                    }

                    break;
                }
                default:
                    Parent.GetLoggerService().Warning("Child is a " + child.GetType());
                    break;
            }
        }
    }

    private void MouseDown3DSector(object sender, MouseDown3DEventArgs args, MouseButtonEventArgs mouseButtonEventArgs)
    {

        if (Header == MeshViewHeaders.SectorPreview && args.HitTestResult.ModelHit is SubmeshComponent { Parent: MeshComponent { Parent: MeshComponent mesh } })
        {
            if (mouseButtonEventArgs.RightButton == MouseButtonState.Pressed)
            {
                Parent.GetLoggerService().Info("RighClick: " + mesh.Text);
            }
            else if (mouseButtonEventArgs.LeftButton == MouseButtonState.Pressed)
            {
                UpdateSelection(mesh);
            }
        }
    }


    #endregion

    #region meshpreviewblock

    public List<Sector> Sectors { get; set; } = new();

    public Vector3 SearchPoint { get; set; } = new();

    public bool SearchActive = false;

    [RelayCommand]
    private void LoadSector(Sector sector)
    {
        if (sector.IsLoaded)
        {
            return;
        }

        var sectorFile = Parent.GetFileFromDepotPathOrCache(sector.DepotPath);

        if (sectorFile?.RootChunk is not worldStreamingSector wss)
        {
            return;
        }

        sector.Element = RenderSector(wss, Appearances[0]);
        sector.Element.Name = sector.Name.Replace("-", "n");
        sector.Element.Text = sector.Name;
        sector.IsLoaded = true;
        sector.ShowElements = true;
        sector.Text.IsRendering = false;
    }

    

    [RelayCommand]
    public void ClearSearch()
    {
        SearchActive = false;
        RenderBlock(_data as worldStreamingBlock);
    }

    [RelayCommand]
    public void SearchForPoint()
    {
        SearchActive = true;
        RenderBlock(_data as worldStreamingBlock);
        CenterCameraToCoord(SearchPoint);
    }

    [RelayCommand]
    public void AddSectorsToProject()
    {
        if (SelectedAppearance == null)
        {
            return;
        }

        foreach (var modelGroup in SelectedAppearance.ModelGroup)
        {
            if (!modelGroup.IsRendering || modelGroup is not SectorGroup sectorGroup)
            {
                continue;
            }

            // TODO: Could check if ep1 is found, not sure though if the same filename could be in both folders...

            var path = (ResourcePath)$@"base\worlds\03_night_city\_compiled\default\ep1\{sectorGroup.Text}.streamingsector";
            _gameController.GetController().AddToMod(path);

            path = (ResourcePath)$@"base\worlds\03_night_city\_compiled\default\{sectorGroup.Text}.streamingsector";
            _gameController.GetController().AddToMod(path);

        }
    }

    public void RenderBlockSolo()
    {
        if (IsRendered)
        {
            return;
        }
        IsRendered = true;
        RenderBlock(_data as worldStreamingBlock);
        SelectWorldNode();
    }

    public void RenderBlock(worldStreamingBlock? data)
    {
        if (data is null)
        {
            return;
        }

        Appearances = new();

        var app = new Appearance("All_Sectors");

        Appearances.Add(app);
        SelectedAppearance = app;

        var texts = new GroupModel3DExt()
        {
            Name = "SectorNames",
            Text = "SectorNames"
        };

        var sectors = new List<Sector>();

        var exterior = new GroupModel3DExt()
        {
            Name = "Exterior",
            Text = "Exterior",
        };
        var exterior_0 = new GroupModel3DExt()
        {
            Name = "Exterior_0",
            Text = "Exterior_0",
        };
        var exterior_1 = new GroupModel3DExt()
        {
            Name = "Exterior_1",
            Text = "Exterior_1",
        };
        var exterior_2 = new GroupModel3DExt()
        {
            Name = "Exterior_2",
            Text = "Exterior_2",
        };
        var exterior_3 = new GroupModel3DExt()
        {
            Name = "Exterior_3",
            Text = "Exterior_3",
        };
        var exterior_4 = new GroupModel3DExt()
        {
            Name = "Exterior_4",
            Text = "Exterior_4",
        };
        var exterior_5 = new GroupModel3DExt()
        {
            Name = "Exterior_5",
            Text = "Exterior_5",
        };
        var exterior_6 = new GroupModel3DExt()
        {
            Name = "Exterior_6",
            Text = "Exterior_6",
        };
        exterior.Children.Add(exterior_0);
        exterior.Children.Add(exterior_1);
        exterior.Children.Add(exterior_2);
        exterior.Children.Add(exterior_3);
        exterior.Children.Add(exterior_4);
        exterior.Children.Add(exterior_5);
        exterior.Children.Add(exterior_6);
        var interior = new GroupModel3DExt()
        {
            Name = "Interior",
            Text = "Interior",
        };
        var quest = new GroupModel3DExt()
        {
            Name = "Quest",
            Text = "Quest",
        };
        var navigation = new GroupModel3DExt()
        {
            Name = "Navigation",
            Text = "Navigation",
        };
        var other = new GroupModel3DExt()
        {
            Name = "Other",
            Text = "Other",
        };

        foreach (var desc in data.Descriptors)
        {
            if (SearchActive && (!(SearchPoint.X < desc.StreamingBox.Max.X) || !(SearchPoint.X > desc.StreamingBox.Min.X) ||
                                 !(SearchPoint.Y < desc.StreamingBox.Max.Y) || !(SearchPoint.Y > desc.StreamingBox.Min.Y) ||
                                 !(SearchPoint.Z < desc.StreamingBox.Max.Z) || !(SearchPoint.Z > desc.StreamingBox.Min.Z)))
            {
                continue;
            }

            var text = new BillboardText3D();
            text.TextInfo.Add(
                new TextInfo(Path.GetFileNameWithoutExtension(desc.Data.DepotPath.GetResolvedText()),
                    new SharpDX.Vector3((desc.StreamingBox.Max.X + desc.StreamingBox.Min.X) / 2, (desc.StreamingBox.Max.Z + desc.StreamingBox.Min.Z) / 2, -(desc.StreamingBox.Max.Y + desc.StreamingBox.Min.Y) / 2))
                {
                    Foreground = SharpDX.Color.Red, Scale = 0.5f
                }
            );

            var bbText = new WKBillboardTextModel3D()
            {
                Geometry = text,
                Name = Path.GetFileNameWithoutExtension(desc.Data.DepotPath.GetResolvedText().NotNull()).Replace("-", "n"),
                Text = Path.GetFileNameWithoutExtension(desc.Data.DepotPath.GetResolvedText().NotNull()),
            };

            if (desc.Category == Enums.worldStreamingSectorCategory.Exterior)
            {
                //var mb = new MeshBuilder();

                //mb.AddBox(text.TextInfo[0].Origin,
                //    desc.StreamingBox.Max.X - desc.StreamingBox.Min.X,
                //    desc.StreamingBox.Max.Y - desc.StreamingBox.Min.Y,
                //    desc.StreamingBox.Max.Z - desc.StreamingBox.Min.Z);
                //mb.ComputeNormalsAndTangents(MeshFaces.Default);

                //var material = SetupPBRMaterial("DefaultMaterial");
                //material.AlbedoColor = new SharpDX.Color4(1f, 1f, 1f, 0.01f);

                //var mesh = new MeshGeometryModel3D()
                //{
                //    Geometry = mb.ToMeshGeometry3D(),
                //    Material = material,
                //    IsTransparent = true
                //};

                if (desc.Level == 0)
                {
                    exterior_0.Children.Add(bbText);
                    //exterior_0.Children.Add(mesh);
                }
                else if (desc.Level == 1)
                {
                    exterior_1.Children.Add(bbText);
                    //exterior_1.Children.Add(mesh);
                }
                else if (desc.Level == 2)
                {
                    exterior_2.Children.Add(bbText);
                    //exterior_2.Children.Add(mesh);
                }
                else if (desc.Level == 3)
                {
                    exterior_3.Children.Add(bbText);
                    //exterior_3.Children.Add(mesh);
                }
                else if (desc.Level == 4)
                {
                    exterior_4.Children.Add(bbText);
                    //exterior_4.Children.Add(mesh);
                }
                else if (desc.Level == 5)
                {
                    exterior_5.Children.Add(bbText);
                    //exterior_5.Children.Add(mesh);
                }
                else if (desc.Level == 6)
                {
                    exterior_6.Children.Add(bbText);
                    //exterior_6.Children.Add(mesh);
                }
                else
                {
                    exterior.Children.Add(bbText);
                    //exterior.Children.Add(mesh);
                }
            }
            else if (desc.Category == Enums.worldStreamingSectorCategory.Interior)
            {
                interior.Children.Add(bbText);
            }
            else if (desc.Category == Enums.worldStreamingSectorCategory.Quest)
            {
                quest.Children.Add(bbText);
            }
            else if (desc.Category == Enums.worldStreamingSectorCategory.Navigation)
            {
                navigation.Children.Add(bbText);
            }
            else
            {
                other.Children.Add(bbText);
            }

            sectors.Add(new Sector(Path.GetFileNameWithoutExtension(desc.Data.DepotPath.GetResolvedText().NotNull()), bbText)
            {
                DepotPath = desc.Data.DepotPath, NumberOfHandles = desc.NumNodeRanges
            });
        }
        Sectors = new List<Sector>(sectors.OrderBy(x => x.Name).ToList());
        texts.Children.Add(exterior);
        texts.Children.Add(interior);
        texts.Children.Add(quest);
        texts.Children.Add(navigation);
        texts.Children.Add(other);
        app.ModelGroup.Add(texts);

    }

    private void MouseDown3DBlock(object sender, MouseDown3DEventArgs args, MouseButtonEventArgs mouseButtonEventArgs)
    {
        if (Header != MeshViewHeaders.AllSectorPreview || mouseButtonEventArgs.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        switch (args.HitTestResult.ModelHit)
        {
            case WKBillboardTextModel3D text:
            {
                var sector = Sectors.FirstOrDefault(x => x.Text == text);
                if (sector is not null)
                {
                    try
                    {
                        LoadSector(sector);
                    }
                    catch (Exception ex)
                    {
                        Parent.GetLoggerService().Error(ex);
                    }
                }

                args.Handled = true;
                break;
            }
            case MeshComponent group:
                SelectedItem = group;
                args.Handled = true;
                break;
            default:
                break;
        }
    }

    #endregion

    #region meshpreviewentity



    public void RenderEntitySolo()
    {
        if (IsRendered)
        {
            return;
        }
        IsRendered = true;
        try
        {
            RenderEntity(_data as entEntityTemplate);
        }
        catch (Exception e)
        {
            _loggerService.Error(
                $"Failed to render entity. Please ensure that all your component names are unique. If they are, open a ticket:  \n${e}");
        }
    }

    public GroupModel3DExt? RenderEntity(entEntityTemplate? ent, Appearance? appearance = null, string? appearanceName = null)
    {
        if (ent?.CompiledData.Data is not RedPackage pkg)
        {
            return null;
        }

        if (ent.Appearances.Count > 0)
        {
            foreach (var component in pkg.Chunks)
            {
                switch (component)
                {
                    case entSlotComponent slotset:
                    {
                        var slots = new Dictionary<string, string>();
                        foreach (var slot in slotset.Slots)
                        {
                            var name = slot.SlotName.ToString().NotNull();

                            if (!slots.ContainsKey(name))
                            {
                                slots.Add(name, slot.BoneName.ToString().NotNull());
                            }
                        }

                        string? bindName = null, slotName = null;
                        if (slotset.ParentTransform?.GetValue() is entHardTransformBinding ehtb)
                        {
                            bindName = ehtb.BindName;
                            slotName = ehtb.SlotName;
                        }

                        var slotSetName = slotset.Name.ToString().NotNull();
                        if (!_slotSets.ContainsKey(slotSetName))
                        {
                            _slotSets.Add(slotSetName,
                                new SlotSet(slotSetName, bindName)
                                {
                                    Matrix = ToSeparateMatrix(slotset.LocalTransform), Slots = slots, SlotName = slotName
                                });
                        }

                        break;
                    }
                    case entAnimatedComponent enc:
                    {
                        var rigFile = Parent.GetFileFromDepotPathOrCache(enc.Rig.DepotPath);

                        if (rigFile?.RootChunk is animRig rig)
                        {
                            var rigBones = new List<RigBone>();
                            for (var i = 0; i < rig.BoneNames.Count; i++)
                            {
                                var rigBone = new RigBone(rig.BoneNames[i].ToString().NotNull())
                                {
                                    Matrix = ToSeparateMatrix(rig.BoneTransforms[i].NotNull())
                                };

                                if (rig.BoneParentIndexes[i] != -1)
                                {
                                    rigBones[rig.BoneParentIndexes[i]].AddChild(rigBone);
                                }

                                rigBones.Add(rigBone);
                            }

                            string? bindName = null, slotName = null;
                            if ((enc.ParentTransform?.GetValue() ?? null) is entHardTransformBinding ehtb)
                            {
                                bindName = ehtb.BindName;
                                slotName = ehtb.SlotName;
                            }

                            Rigs[enc.Name.ToString().NotNull()] = new Rig(enc.Name.ToString().NotNull())
                            {
                                Bones = rigBones, BindName = bindName, SlotName = slotName
                            };
                        }

                        break;
                    }

                    default:
                        break;
                }
            }

            foreach (var rig in Rigs.Values)
            {
                if (rig.BindName is not null && Rigs.ContainsKey(rig.BindName))
                {
                    Rigs[rig.BindName].AddChild(rig);
                }
            }

            List<entTemplateAppearance> appearances;
            if (appearance != null)
            {
                appearances = ent.Appearances.Where(x => x is not null && x.Name == ent.DefaultAppearance).ToList();
            }
            else if (appearanceName == null)
            {
                appearances = ent.Appearances.ToList();
            }
            else
            {
                appearances = ent.Appearances.Where(x => x is not null && x.AppearanceName == appearanceName).ToList();
            }

            var element = new GroupModel3DExt();

            var idx = -1;
            foreach (var app in appearances)
            {
                idx++;
                if (app is null)
                {
                    _loggerService.Error($"appearance {idx} is null! Skipping...");
                    continue;
                }

                var appFile = Parent.GetFileFromDepotPathOrCache(app.AppearanceResource.DepotPath);

                if (appFile is not { RootChunk: appearanceAppearanceResource aar })
                {
                    _loggerService.Error($"Failed to laod appearance {idx} from {app.AppearanceResource.DepotPath}");
                    continue;
                }

                // ArchiveXL dynamic variants will reuse name on empty appearance name 
                if (app.AppearanceName == CName.Empty)
                {
                    app.AppearanceName = app.Name;
                }

                var appearanceDefs = aar.Appearances
                    .Where(handle => handle?.GetValue() is appearanceAppearanceDefinition)
                    .Select(handle => (appearanceAppearanceDefinition)handle.GetValue()!)
                    .GroupBy(value => value.Name.GetResolvedText()!)
                    .ToDictionary(group => group.Key, group => group.First());

                if (!appearanceDefs.TryGetValue(app.AppearanceName.GetResolvedText() ?? "invalid name", out var appDef) ||
                    appDef.CompiledData?.Data is not RedPackage appPkg)
                {
                    _loggerService.Error(
                        $"No valid appearance with the name {app.AppearanceName} found in {app.AppearanceResource.DepotPath}");
                    continue;
                }

                {
                    var loadableModels = LoadMeshes(appPkg.Chunks);
                    loadableModels.AddRange(LoadPartsValues(appDef));
                    LoadPartsOverrides(appDef, loadableModels);
                    
                    var a = new Appearance(app.Name.ToString().NotNull())
                    {
                        AppearanceName = app.AppearanceName,
                        Resource = app.AppearanceResource.DepotPath,
                        Models = loadableModels,
                    };

                    foreach (var model in a.Models)
                    {
                        if (a.Models.FirstOrDefault(x => x.Name == model.BindName) is var parentModel && parentModel is not null)
                        {
                            parentModel.AddModel(model);
                        }
                        else
                        {
                            a.BindableModels.Add(model);
                        }
                        foreach (var material in model.Materials)
                        {
                            a.RawMaterials[material.Name] = material;
                        }
                        if (model.MeshFile?.RootChunk is CMesh mesh)
                        {
                            model.Meshes = MakeMesh(mesh, model.ChunkMask, model.AppearanceIndex);
                        }

                        foreach (var m in model.Meshes)
                        {
                            if (!a.LODLUT.TryGetValue(m.LOD, out var value))
                            {
                                value = new List<SubmeshComponent>();
                                a.LODLUT[m.LOD] = value;
                            }

                            value.Add(m);
                        }
                    }

                    if (appearance == null)
                    {
                        a.ModelGroup.AddRange(AddMeshesToRiggedGroups(a));
                        Appearances.Add(a);
                    }
                    else
                    {
                        //appearance.ModelGroup.AddRange(a.ModelGroup);
                        var group = AddMeshesToRiggedGroups(a);
                        foreach (var model in group)
                        {
                            element.Children.Add(model);
                        }
                    }
                }

            }


            if (appearance == null && Appearances.Count > 0)
            {
                SelectedAppearance = Appearances[0];
            }
            
            return element;
        }

        // ent.appearances.Count == null => it's not a root entity
        var models = LoadMeshes(pkg.Chunks);

        if (models.Count == 0)
        {
            return null;
        }

        var meshApp = appearance ?? new Appearance("Default") { Models = models };

        var cGroup = new MeshComponent() { WorldNodeIndex = string.Empty, WorldNodeDataIndices = string.Empty, };

        foreach (var model in models)
        {
            foreach (var material in model.Materials)
            {
                meshApp.RawMaterials[material.Name] = material;
            }

            if (model.MeshFile?.RootChunk is CMesh mesh)
            {
                model.Meshes = MakeMesh(mesh, model.ChunkMask, model.AppearanceIndex);
            }

            foreach (var m in model.Meshes)
            {
                cGroup.Children.Add(m);
                if (!meshApp.LODLUT.ContainsKey(m.LOD))
                {
                    meshApp.LODLUT[m.LOD] = new List<SubmeshComponent>();
                }

                meshApp.LODLUT[m.LOD].Add(m);
            }
        }

        if (appearance == null)
        {
            meshApp.ModelGroup.Add(cGroup);
            Appearances.Add(meshApp);
            SelectedAppearance = meshApp;
        }

        var el = new GroupModel3DExt();
        foreach (var model in meshApp.ModelGroup)
        {
            el.Children.Add(model);
        }

        return el;
        
    }

    #endregion

    private object? findMeshChild(object o)
    {
        switch (o)
        {
            case SectorGroup group:
            {
                foreach (var groupChild in group.Children)
                {
                    if (findMeshChild(groupChild) is { } result)
                    {
                        return result;
                    }
                }

                break;
            }
            case LoadableModel loadableModel:
                return loadableModel;
            case MeshComponent comp:
                if (comp.WorldNodeIndex == SelectedNodeIndex)
                {
                    return comp;
                }

                // foreach (var compChild in comp.Children)
                // {
                //     if (findMeshChild(compChild) is { } found)
                //     {
                //         return found;
                //     }
                // }

                break;
            default:
                break;
        }

        return null;
    }
    public void SelectWorldNode()
    {
        if (SelectedNodeIndex is null)
        {
            return;
        }

        object? selectedMesh = null;
        foreach (var loadableModel in Models)
        {
            if (selectedMesh is not null)
            {
                continue;
            }

            selectedMesh = findMeshChild(loadableModel);
        }

        foreach (var appearance in Appearances)
        {
            appearance.ModelGroup.AsList().ForEach((model) =>
            {
                if (selectedMesh is not null)
                {
                    return;
                }

                selectedMesh = findMeshChild(model);
            });
        }

        SelectedItem = selectedMesh;
    }
}
