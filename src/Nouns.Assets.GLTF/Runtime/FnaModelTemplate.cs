﻿#if USINGMONOGAMEMODEL
using MODELMESH = Microsoft.Xna.Framework.Graphics.ModelMesh;
using MODELMESHPART = Microsoft.Xna.Framework.Graphics.ModelMeshPart;
#else
using MODELMESH = Nouns.Assets.GLTF.Runtime.RuntimeModelMesh;
#endif
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using SharpGLTF.Runtime;

namespace Nouns.Assets.GLTF.Runtime
{
    public class FnaModelTemplate
    {
        #region lifecycle

        public static FnaDeviceContent<FnaModelTemplate> LoadDeviceModel(GraphicsDevice device, string filePath, LoaderContext context = null)
        {
            var model = SharpGLTF.Schema2.ModelRoot.Load(filePath, SharpGLTF.Validation.ValidationMode.TryFix);

            return CreateDeviceModel(device, model, context);
        }

        public static FnaDeviceContent<FnaModelTemplate> CreateDeviceModel(GraphicsDevice device, SharpGLTF.Schema2.ModelRoot srcModel, LoaderContext context = null)
        {
            if (context == null) context = new BasicEffectsLoaderContext(device);

            context.Reset();

            var options = new SharpGLTF.Runtime.RuntimeOptions { IsolateMemory = true };

            var templates = srcModel.LogicalScenes
                .Select(item => SceneTemplate.Create(item, options))
                .ToArray();            

            var srcMeshes = templates
                .SelectMany(item => item.LogicalMeshIds)
                .Distinct()
                .Select(idx => srcModel.LogicalMeshes[idx]);

            foreach(var srcMesh in srcMeshes)
            {
                context._WriteMesh(srcMesh);
            }

            var dstMeshes = context.CreateRuntimeModels();

            var mdl = new FnaModelTemplate(templates,srcModel.DefaultScene.LogicalIndex, dstMeshes);

            return new FnaDeviceContent<FnaModelTemplate>(mdl, context.Disposables.ToArray());
        }
        
        internal FnaModelTemplate(SceneTemplate[] scenes, int defaultSceneIndex, IReadOnlyDictionary<int, MODELMESH> meshes)
        {
            _Meshes = meshes;
            _Effects = _Meshes.Values
                .SelectMany(item => item.Effects)
                .Distinct()
                .ToArray();

            _Scenes = scenes;
            _Bounds = scenes
                .Select(item => CalculateBounds(item))
                .ToArray();

            _DefaultSceneIndex = defaultSceneIndex;
        }

        #endregion

        #region data
        
        /// <summary>
        /// Meshes shared by all the scenes.
        /// </summary>
        internal readonly IReadOnlyDictionary<int, MODELMESH> _Meshes;

        /// <summary>
        /// Effects shared by all the meshes.
        /// </summary>
        private readonly Effect[] _Effects;

        private readonly SceneTemplate[] _Scenes;
        private readonly BoundingSphere[] _Bounds;

        private readonly int _DefaultSceneIndex;

        #endregion

        #region properties

        public int SceneCount => _Scenes.Length;

        public IReadOnlyList<Effect> Effects => _Effects;

        public BoundingSphere Bounds => GetBounds(_DefaultSceneIndex);        
        
        #endregion

        #region API

        public int IndexOfScene(string sceneName) => Array.FindIndex(_Scenes, item => item.Name == sceneName);

        public BoundingSphere GetBounds(int sceneIndex) => _Bounds[sceneIndex];        

        public FnaModelInstance CreateInstance() => CreateInstance(_DefaultSceneIndex);

        public FnaModelInstance CreateInstance(int sceneIndex)
        {
            return new FnaModelInstance(this, _Scenes[sceneIndex].CreateInstance());
        }

        private BoundingSphere CalculateBounds(SceneTemplate scene)
        {
            var instances = scene.CreateInstance();            

            var bounds = default(BoundingSphere);

            foreach (var inst in instances)
            {
                var b = _Meshes[inst.Template.LogicalMeshIndex].BoundingSphere;

                if (inst.Transform is SharpGLTF.Transforms.RigidTransform statXform) b = b.Transform(statXform.WorldMatrix.ToXna());

                if (inst.Transform is SharpGLTF.Transforms.SkinnedTransform skinXform)
                {
                    // this is a bit aggressive and probably over-reaching, but with skins you never know the actual bounds
                    // unless you calculate the bounds frame by frame.

                    var bb = b;

                    foreach (var xb in skinXform.SkinMatrices.Select(item => bb.Transform(item.ToXna())))
                    {
                        b = BoundingSphere.CreateMerged(b, xb);
                    }
                }

                bounds = bounds.Radius == 0 ? b : BoundingSphere.CreateMerged(bounds, b);
            }

            return bounds;
        }
        
        #endregion        
    }    
}
