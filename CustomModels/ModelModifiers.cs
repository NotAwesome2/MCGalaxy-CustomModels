using System;
using System.Collections.Generic;

namespace MCGalaxy {
    public sealed partial class CustomModelsPlugin {
        public class ModelModifiers {
            private readonly HashSet<string> modifiers;
            private readonly CustomModel model;
            private readonly List<Part> parts;
            private readonly BlockBench.JsonRoot blockBench;

            ModelModifiers(HashSet<string> modifiers, CustomModel model, List<Part> parts, BlockBench.JsonRoot blockBench) {
                this.modifiers = modifiers;
                this.model = model;
                this.parts = parts;
                this.blockBench = blockBench;
            }

            public static void Apply(HashSet<string> modifiers, CustomModel model, List<Part> parts, BlockBench.JsonRoot blockBench) {
                new ModelModifiers(modifiers, model, parts, blockBench).Apply();
            }

            public void Apply() {
                if (modifiers.Contains("sitcute")) {
                    ApplySit(true);
                } else if (modifiers.Contains("sit")) {
                    ApplySit();
                }

                if (StoredCustomModel.UsesHumanParts(parts)) {
                    if (modifiers.Contains("alex")) {
                        // our entity is using an alex skin, convert model from SteveLayers to Alex
                        ApplyAlex();
                    } else if (modifiers.Contains("steve")) {
                        // our entity is using a steve skin, convert from SteveLayers to Steve
                        ApplySteve();
                    }
                }
            }

            void ApplySit(bool isCute = false) {
                float? leftLegMaxY = null;
                float? leftLegMinY = null;
                float? leftLegMaxZ = null;
                float? leftLegMinZ = null;

                float? rightLegMaxY = null;
                float? rightLegMinY = null;
                float? rightLegMaxZ = null;
                float? rightLegMinZ = null;

                foreach (var part in parts) {
                    foreach (var anim in part.anims) {
                        if (anim.type == CustomModelAnimType.LeftLegX) {
                            // find very top and bottom points of all leg parts
                            if (!leftLegMaxY.HasValue || part.max.Y > leftLegMaxY) leftLegMaxY = part.max.Y;
                            if (!leftLegMinY.HasValue || part.min.Y < leftLegMinY) leftLegMinY = part.min.Y;
                            // only use 1 part for finding width of leg
                            leftLegMaxZ = part.max.Z;
                            leftLegMinZ = part.min.Z;
                        } else if (anim.type == CustomModelAnimType.RightLegX) {
                            if (!rightLegMaxY.HasValue || part.max.Y > rightLegMaxY) rightLegMaxY = part.max.Y;
                            if (!rightLegMinY.HasValue || part.min.Y < rightLegMinY) rightLegMinY = part.min.Y;
                            rightLegMaxZ = part.max.Z;
                            rightLegMinZ = part.min.Z;
                        }

                        if (
                            anim.type == CustomModelAnimType.LeftLegX ||
                            anim.type == CustomModelAnimType.RightLegX
                        ) {
                            // rotate legs to point forward, pointed a little outwards
                            part.rotation.X = 90.0f;
                            if (isCute) {
                                part.rotation.Y = anim.type == CustomModelAnimType.LeftLegX ? 1.0f : -1.0f;
                            } else {
                                part.rotation.Y = anim.type == CustomModelAnimType.LeftLegX ? 5.0f : -5.0f;
                            }
                            part.rotation.Z = 0;
                            anim.type = CustomModelAnimType.None;
                        }
                    }
                }

                var leftLegHeight = leftLegMaxY - leftLegMinY;
                var leftLegForwardWidth = leftLegMaxZ - leftLegMinZ;

                var rightLegHeight = rightLegMaxY - rightLegMinY;
                var rightLegForwardWidth = rightLegMaxZ - rightLegMinZ;

                // lower all parts by leg's Y height, up by the leg's width
                var leftLower = leftLegHeight - leftLegForwardWidth / 2.0f;
                var rightLower = rightLegHeight - rightLegForwardWidth / 2.0f;

                var lower = leftLower.HasValue ? leftLower : rightLower;
                if (lower.HasValue) {
                    foreach (var part in parts) {
                        part.min.Y -= lower.Value;
                        part.max.Y -= lower.Value;
                        part.rotationOrigin.Y -= lower.Value;

                        if (part.firstPersonArm) {
                            // remove first person arm because offset changed
                            part.firstPersonArm = false;
                        }
                    }
                    model.eyeY -= lower.Value;
                    model.nameY -= lower.Value;
                }

                if (isCute) {
                    foreach (var part in parts) {
                        var appliedOffset = false;
                        foreach (var anim in part.anims) {
                            var left = anim.type == CustomModelAnimType.LeftArmX || anim.type == CustomModelAnimType.LeftArmZ;
                            var right = anim.type == CustomModelAnimType.RightArmX || anim.type == CustomModelAnimType.RightArmZ;
                            if (left || right) {
                                // rotate legs to point forward, pointed a little outwards
                                part.rotation.X = 50.0f;
                                part.rotation.Y = left ? -40.0f : 40.0f;
                                part.rotation.Z = left ? -25.0f : 25.0f;
                                if (!appliedOffset) {
                                    part.max.Z += -0.5f / 16.0f;
                                    part.min.Z += -0.5f / 16.0f;
                                    appliedOffset = true;
                                }
                                anim.type = CustomModelAnimType.None;
                            }
                        }
                    }
                }
            }

            void ApplyAlex() {
                foreach (var part in parts) {
                    if (part.skinLeftArm || part.skinRightArm) {
                        // top
                        part.u1[0] -= 1;

                        // down
                        part.u1[1] -= 1;
                        part.u2[1] -= 2;

                        // north
                        part.u1[2] -= 1;

                        // south
                        part.u1[3] -= 2;
                        part.u2[3] -= 1;

                        // east
                        part.u1[5] -= 1;
                        part.u2[5] -= 1;
                    }
                    if (part.skinLeftArm) {
                        part.min.X += 1.0f / 16.0f;
                    } else if (part.skinRightArm) {
                        part.max.X -= 1.0f / 16.0f;
                    }
                }
            }

            void ApplySteve() {
                // remove layer parts
                for (int i = parts.Count - 1; i >= 0; i--) {
                    var part = parts[i];
                    if (part.layer) {
                        parts.RemoveAt(i);
                    }
                }

                // halve all uv "y coord"/v
                foreach (var part in parts) {
                    part.v1[0] *= 2;
                    part.v1[1] *= 2;
                    part.v1[2] *= 2;
                    part.v1[3] *= 2;
                    part.v1[4] *= 2;
                    part.v1[5] *= 2;

                    part.v2[0] *= 2;
                    part.v2[1] *= 2;
                    part.v2[2] *= 2;
                    part.v2[3] *= 2;
                    part.v2[4] *= 2;
                    part.v2[5] *= 2;
                }

                void f(Part left, Part right) {
                    // there's only 1 leg/arm in the steve model
                    left.u1 = (ushort[])right.u1.Clone();
                    left.u2 = (ushort[])right.u2.Clone();
                    left.v1 = (ushort[])right.v1.Clone();
                    left.v2 = (ushort[])right.v2.Clone();

                    // swap u's
                    Swap(ref left.u1, ref left.u2);

                    /* uv coords in order: top, bottom, front, back, left, right */
                    // swap west and east
                    Swap(ref left.u1[4], ref left.u1[5]);
                    Swap(ref left.u2[4], ref left.u2[5]);
                    Swap(ref left.v1[4], ref left.v1[5]);
                    Swap(ref left.v2[4], ref left.v2[5]);
                }

                Part leftArm = parts.Find(part => part.skinLeftArm);
                Part rightArm = parts.Find(part => part.skinRightArm);
                if (leftArm != null && rightArm != null) {
                    f(leftArm, rightArm);
                }

                Part leftLeg = parts.Find(part => part.skinLeftLeg);
                Part rightLeg = parts.Find(part => part.skinRightLeg);
                if (leftLeg != null && rightLeg != null) {
                    f(leftLeg, rightLeg);
                }
            }


        }

        class BlockBenchModifiers {
            private readonly HashSet<string> modifiers;
            private readonly StoredCustomModel storedCustomModel;
            private readonly BlockBench.JsonRoot blockBench;

            BlockBenchModifiers(HashSet<string> modifiers, StoredCustomModel storedCustomModel, BlockBench.JsonRoot blockBench) {
                this.modifiers = modifiers;
                this.storedCustomModel = storedCustomModel;
                this.blockBench = blockBench;
            }

            public static void Apply(HashSet<string> modifiers, StoredCustomModel storedCustomModel, BlockBench.JsonRoot blockBench) {
                new BlockBenchModifiers(modifiers, storedCustomModel, blockBench).Apply();
            }

            public void Apply() {
                if (HttpSkinServer.ShouldUseCustomURL(blockBench)) {
                    ApplyMerged();
                }
            }

            void ApplyMerged() {
                var textureSheet = TextureSheet.FromTextures(blockBench.textures);

                var x = 0;
                foreach (var textureImage in textureSheet.textureImages) {
                    var image = textureImage.image;

                    void UpdateFace(BlockBench.JsonRoot.Face face) {
                        if (face.texture == textureImage.id) {
                            var u1 = face.uv[0] * (image.Width / (float)blockBench.resolution.width);
                            var v1 = face.uv[1] * (image.Height / (float)blockBench.resolution.height);
                            var u2 = face.uv[2] * (image.Width / (float)blockBench.resolution.width);
                            var v2 = face.uv[3] * (image.Height / (float)blockBench.resolution.height);

                            u1 += x;
                            u2 += x;

                            face.uv[0] = u1;
                            face.uv[1] = v1;
                            face.uv[2] = u2;
                            face.uv[3] = v2;
                        }
                    }

                    foreach (var element in blockBench.elements) {
                        UpdateFace(element.faces.up);
                        UpdateFace(element.faces.down);
                        UpdateFace(element.faces.north);
                        UpdateFace(element.faces.south);
                        UpdateFace(element.faces.east);
                        UpdateFace(element.faces.west);
                    }

                    x += image.Width;
                }

                blockBench.resolution.width = (UInt16)textureSheet.width;
                blockBench.resolution.height = (UInt16)textureSheet.height;

                textureSheet.Dispose();
            }

        }
    }
}
