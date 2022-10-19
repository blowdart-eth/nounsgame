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
    public sealed class FnaModelInstance
    {
        #region lifecycle

        internal FnaModelInstance(FnaModelTemplate template, SceneInstance instance)
        {
            _Template = template;
            _Controller = instance;
        }

        #endregion

        #region data

        private readonly FnaModelTemplate _Template;
        private readonly SceneInstance _Controller;

        #endregion

        #region properties

        /// <summary>
        /// Gets a reference to the template used to create this <see cref="FnaModelInstance"/>.
        /// </summary>
        public FnaModelTemplate Template => _Template;

        /// <summary>
        /// Gets a reference to the animation controller of this <see cref="FnaModelInstance"/>.
        /// </summary>
        public SceneInstance Controller => _Controller;

        #endregion

        #region API

        /// <summary>
        /// Draws this <see cref="FnaModelInstance"/> into the current <see cref="GraphicsDevice"/>.
        /// </summary>
        /// <param name="projection">The projection matrix.</param>
        /// <param name="view">The view matrix.</param>
        /// <param name="world">The world matrix.</param>
        public void Draw(Matrix projection, Matrix view, Matrix world)
        {
            foreach (var inst in _Controller)
            {
                Draw(_Template._Meshes[inst.Template.LogicalMeshIndex], projection, view, world, inst.Transform);
            }
        }

        private void Draw(MODELMESH mesh, Matrix projectionXform, Matrix viewXform, Matrix worldXform, SharpGLTF.Transforms.IGeometryTransform modelXform)
        {
            if (modelXform is SharpGLTF.Transforms.SkinnedTransform skinXform)
            {
                var skinTransforms = skinXform.SkinMatrices.Select(item => item.ToXna()).ToArray();

                foreach (var effect in mesh.Effects)
                {
                    UpdateTransforms(effect, projectionXform, viewXform, worldXform, skinTransforms);
                }
            }

            if (modelXform is SharpGLTF.Transforms.RigidTransform statXform)
            {
                worldXform = Matrix.Multiply(statXform.WorldMatrix.ToXna(), worldXform);

                foreach (var effect in mesh.Effects)
                {
                    UpdateTransforms(effect, projectionXform, viewXform, worldXform);
                }
            }

            mesh.Draw();
        }

        private static void UpdateTransforms(Effect effect, Matrix projectionXform, Matrix viewXform, Matrix worldXform, Matrix[] skinTransforms = null)
        {
            if (effect is IEffectMatrices matrices)
            {
                matrices.Projection = projectionXform;
                matrices.View = viewXform;
                matrices.World = worldXform;
            }

            if (effect is SkinnedEffect skin && skinTransforms != null)
            {
                var xposed = skinTransforms.Select(item => Matrix.Transpose(item)).ToArray();

                skin.SetBoneTransforms(skinTransforms);
            }

            if (effect is IEffectBones iskin && skinTransforms != null)
            {
                var xposed = skinTransforms.Select(item => Matrix.Transpose(item)).ToArray();

                iskin.SetBoneTransforms(skinTransforms, 0, skinTransforms.Length);
            }            
        }

        #endregion
    }    
}
