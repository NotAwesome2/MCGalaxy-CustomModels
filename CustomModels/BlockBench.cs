using System;
using System.Collections.Generic;
using MCGalaxy.Commands;
using MCGalaxy.Maths;
using MCGalaxy.Network;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MCGalaxy {
    public sealed partial class CustomModelsPlugin {
        private class Vec3F32Converter : JsonConverter {
            public override bool CanConvert(Type objectType) {
                return objectType == typeof(Vec3F32);
            }

            public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer) {
                var obj = JObject.Load(reader);
                return new Vec3F32 {
                    X = (float)obj["X"],
                    Y = (float)obj["Y"],
                    Z = (float)obj["Z"]
                };
            }

            public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer) {
                var vec = (Vec3F32)value;
                serializer.Serialize(
                    writer,
                    new {
                        vec.X,
                        vec.Y,
                        vec.Z
                    }
                );
            }
        }

        private static readonly JsonSerializerSettings jsonSettings = new JsonSerializerSettings {
            Converters = new[] { new Vec3F32Converter() }
        };

        // ignore "Field is never assigned to"
#pragma warning disable 0649
        private class BlockBench {

            public static bool IsValid(string json, Player p, string modelName) {
                var jsonRoot = Parse(json);
                var parts = jsonRoot.ToParts();

                if (!jsonRoot.IsValid(p)) {
                    return false;
                }

                if (parts.Length > Packet.MaxCustomModelParts) {
                    p.Message(
                        "%WNumber of model parts ({0}) exceeds max of {1}!",
                        parts.Length,
                        Packet.MaxCustomModelParts
                    );
                    return false;
                }

                // only do size check if they can't upload global models
                if (!CmdBypassModelSizeLimit.CanBypassSizeLimit(p)) {
                    for (int i = 0; i < parts.Length; i++) {
                        // Models can be 1 block bigger if they aren't a purely personal model
                        bool purePersonal = new StoredCustomModel(modelName).IsPersonalPrimary();
                        float graceLength = purePersonal ? 8.0f : 16.0f;

                        if (
                            !SizeAllowed(parts[i].min, graceLength) ||
                            !SizeAllowed(parts[i].max, graceLength)
                        ) {
                            p.Message(
                                "%WThe %b{0} cube in your list %Wis out of bounds.",
                                ListicleNumber(i + 1)
                            );
                            p.Message(
                                "%WYour {0} may not be larger than %b{1}%W pixels tall or %b{2}%W pixels wide.",
                                purePersonal ? "personal model" : "model",
                                maxHeight + graceLength,
                                maxWidth + (graceLength * 2),
                                graceLength
                            );

                            if (purePersonal) {
                                p.Message("These limits only apply to your personal \"%b{0}%S\" model.", modelName);
                                p.Message("Models you upload with other names (e.g, /cm {0}bike upload) can be slightly larger.", modelName);
                            }
                            return false;
                        }
                    }
                }

                for (int i = 0; i < parts.Length; i++) {
                    var part = parts[i];
                    if (part.anims.Length > Packet.MaxCustomModelAnims) {
                        p.Message(
                            "%WThe %b{0} cube in your list %Whas more than %b{1} %Wanimations.",
                            ListicleNumber(i + 1),
                            Packet.MaxCustomModelAnims
                        );
                        break;
                    }
                }

                return true;
            }


            // measured in pixels where 16 pixels = 1 block's length
            public const float maxWidth = 16;
            public const float maxHeight = 32;

            // graceLength is how far (in pixels) you can extend past max width/height on all sides
            private static bool SizeAllowed(Vec3F32 boxCorner, float graceLength) {
                //convert to block-unit to match boxCorner
                const float maxWidthB = maxWidth / 16f;
                const float maxHeightB = maxHeight / 16f;
                float graceLengthB = graceLength / 16f;
                if (
                    boxCorner.Y < -graceLengthB ||
                    boxCorner.Y > maxHeightB + graceLengthB ||

                    boxCorner.X < -((maxWidthB / 2) + graceLengthB) ||
                    boxCorner.X > (maxWidthB / 2) + graceLengthB ||
                    boxCorner.Z < -((maxWidthB / 2) + graceLengthB) ||
                    boxCorner.Z > (maxWidthB / 2) + graceLengthB
                ) {
                    return false;
                }
                return true;
            }


            public class JsonRoot {
                public Meta meta;
                public string name;
                public Element[] elements;
                public UuidOrGroup[] outliner;
                public Resolution resolution;

                public bool IsValid(Player p) {
                    // warn player if unsupported features were used
                    bool warnings = false;

                    // check if not free model
                    if (this.meta.model_format != "free") {
                        p.Message(
                            "%WModel uses format %b{0} %W(should be using %b\"Free Model\"%W)!",
                            this.meta.model_format
                        );
                        warnings = true;
                    }

                    string lastTexture = null;
                    string bad(Face face) {
                        // check for uv rotation
                        if (face.rotation != 0) {
                            return "uses UV rotation";
                        }

                        // check for no assigned texture
                        if (face.texture == null) {
                            return "doesn't have a texture";
                        } else {
                            // check if using more than 1 texture
                            if (lastTexture != null) {
                                if (lastTexture != face.texture) {
                                    return "uses multiple textures";
                                }
                            } else {
                                lastTexture = face.texture;
                            }
                        }

                        if (
                            (face.uv[0] % 1) != 0 ||
                            (face.uv[1] % 1) != 0 ||
                            (face.uv[2] % 1) != 0 ||
                            (face.uv[3] % 1) != 0
                        ) {
                            return "uses a non-integer number in UV coordinates";
                        }

                        return null;
                    }
                    for (int i = 0; i < this.elements.Length; i++) {
                        var e = this.elements[i];
                        string reason;

                        void warn(string faceName) {
                            p.Message(
                                "%WThe %b{0} %Wface on the %b{1} %Wcube {2}!",
                                faceName,
                                e.name,
                                reason
                            );
                            warnings = true;
                        }

                        reason = bad(e.faces.up);
                        if (reason != null) { warn("up"); }
                        reason = bad(e.faces.down);
                        if (reason != null) { warn("down"); }
                        reason = bad(e.faces.north);
                        if (reason != null) { warn("north"); }
                        reason = bad(e.faces.south);
                        if (reason != null) { warn("south"); }
                        reason = bad(e.faces.east);
                        if (reason != null) { warn("east"); }
                        reason = bad(e.faces.west);
                        if (reason != null) { warn("west"); }
                    }


                    void test(UuidOrGroup uuidOrGroup) {
                        if (uuidOrGroup.group != null) {
                            var g = uuidOrGroup.group;
                            // we can't support nested rotation & pivot because
                            // there is no way to flatten multiple layers of pivots into
                            // a single pivot and rotation

                            // if pivot point exists, and rotation isn't 0
                            if (
                                g.rotation[0] != 0 ||
                                g.rotation[1] != 0 ||
                                g.rotation[2] != 0
                            ) {
                                p.Message(
                                    "%WThe %b{0} %Wgroup uses rotation!",
                                    g.name
                                );
                                warnings = true;
                            }

                            foreach (var innerGroup in uuidOrGroup.group.children) {
                                test(innerGroup);
                            }
                        }
                    }
                    foreach (var uuidOrGroup in this.outliner) {
                        test(uuidOrGroup);
                    }

                    if (warnings) {
                        p.Message("%WThese BlockBench features aren't supported in ClassiCube.");
                    }


                    return true;
                }

                public Part[] ToParts() {
                    var parts = new List<Part>();

                    var elementByUuid = new Dictionary<string, Element>();
                    foreach (Element e in this.elements) {
                        elementByUuid.Add(e.uuid, e);
                    }

                    foreach (var uuidOrGroup in this.outliner) {
                        HandleGroup(
                            uuidOrGroup,
                            elementByUuid,
                            parts,
                            new[] { 0.0f, 0.0f, 0.0f },
                            new[] { 0.0f, 0.0f, 0.0f },
                            true
                        );
                    }

                    return parts.ToArray();
                }

                private void HandleGroup(
                    UuidOrGroup uuidOrGroup,
                    Dictionary<string, Element> elementByUuid,
                    List<Part> parts,
                    float[] rotation,
                    float[] origin,
                    bool visibility
                ) {
                    if (uuidOrGroup.group == null) {
                        // a uuid

                        var e = elementByUuid[uuidOrGroup.uuid];
                        e.rotation[0] += rotation[0];
                        e.rotation[1] += rotation[1];
                        e.rotation[2] += rotation[2];
                        e.origin[0] += origin[0];
                        e.origin[1] += origin[1];
                        e.origin[2] += origin[2];
                        if (!visibility) {
                            e.visibility = visibility;
                        }
                        var part = ToPart(e);
                        if (part != null) {
                            parts.Add(part);
                        }
                    } else {
                        // a group

                        var totalRotation = new[] {
                            uuidOrGroup.group.rotation[0] + rotation[0],
                            uuidOrGroup.group.rotation[1] + rotation[1],
                            uuidOrGroup.group.rotation[2] + rotation[2],
                        };
                        // var totalOrigin = new[] {
                        //     uuidOrGroup.group.origin[0] + origin[0],
                        //     uuidOrGroup.group.origin[1] + origin[1],
                        //     uuidOrGroup.group.origin[2] + origin[2],
                        // };
                        foreach (var innerGroup in uuidOrGroup.group.children) {
                            HandleGroup(
                                innerGroup,
                                elementByUuid,
                                parts,
                                totalRotation,
                                origin,
                                uuidOrGroup.group.visibility
                            );
                        }
                    }
                }

                private Part ToPart(Element e) {
                    if (!e.visibility) {
                        return null;
                    }

                    Vec3F32 rotation = new Vec3F32 { X = 0, Y = 0, Z = 0 };
                    if (e.rotation != null) {
                        rotation.X = e.rotation[0];
                        rotation.Y = e.rotation[1];
                        rotation.Z = e.rotation[2];
                    }

                    Vec3F32 min = new Vec3F32 {
                        X = (e.from[0] - e.inflate) / 16.0f,
                        Y = (e.from[1] - e.inflate) / 16.0f,
                        Z = (e.from[2] - e.inflate) / 16.0f,
                    };
                    Vec3F32 max = new Vec3F32 {
                        X = (e.to[0] + e.inflate) / 16.0f,
                        Y = (e.to[1] + e.inflate) / 16.0f,
                        Z = (e.to[2] + e.inflate) / 16.0f,
                    };

                    var rotationOrigin = new Vec3F32 {
                        X = e.origin[0] / 16.0f,
                        Y = e.origin[1] / 16.0f,
                        Z = e.origin[2] / 16.0f,
                    };

                    // faces in order [u1, v1, u2, v2]
                    /* uv coords in order: top, bottom, front, back, left, right */
                    // swap up's uv's
                    UInt16[] u1 = new[] {
                        (UInt16)e.faces.up.uv[2],
                        (UInt16)e.faces.down.uv[0],
                        (UInt16)e.faces.north.uv[0],
                        (UInt16)e.faces.south.uv[0],
                        (UInt16)e.faces.east.uv[0],
                        (UInt16)e.faces.west.uv[0],
                    };
                    UInt16[] v1 = new[] {
                        (UInt16)e.faces.up.uv[3],
                        (UInt16)e.faces.down.uv[1],
                        (UInt16)e.faces.north.uv[1],
                        (UInt16)e.faces.south.uv[1],
                        (UInt16)e.faces.east.uv[1],
                        (UInt16)e.faces.west.uv[1],
                    };
                    UInt16[] u2 = new[] {
                        (UInt16)e.faces.up.uv[0],
                        (UInt16)e.faces.down.uv[2],
                        (UInt16)e.faces.north.uv[2],
                        (UInt16)e.faces.south.uv[2],
                        (UInt16)e.faces.east.uv[2],
                        (UInt16)e.faces.west.uv[2],
                    };
                    UInt16[] v2 = new[] {
                        (UInt16)e.faces.up.uv[1],
                        (UInt16)e.faces.down.uv[3],
                        (UInt16)e.faces.north.uv[3],
                        (UInt16)e.faces.south.uv[3],
                        (UInt16)e.faces.east.uv[3],
                        (UInt16)e.faces.west.uv[3],
                    };

                    var part = new Part {
                        name = e.name,
                        min = min,
                        max = max,
                        u1 = u2,
                        v1 = v1,
                        u2 = u1,
                        v2 = v2,
                        rotationOrigin = rotationOrigin,
                        rotation = rotation,
                    };

                    var anims = new List<CustomModelAnim>();
                    var partName = e.name.Replace(" ", "");
                    foreach (var attr in partName.SplitComma()) {
                        float? a = null;
                        float? b = null;
                        float? c = null;
                        float? d = null;

                        var colonSplit = attr.Split(':');
                        var attrName = colonSplit[0];
                        if (colonSplit.Length >= 2) {
                            var modifiers = colonSplit[1].Replace(" ", "").Split('|');
                            if (modifiers.Length > 0) {
                                //Invariant because we must force . as a decimal separator rather than ,
                                a = float.Parse(modifiers[0], System.Globalization.NumberFormatInfo.InvariantInfo);
                                if (modifiers.Length > 1) {
                                    b = float.Parse(modifiers[1], System.Globalization.NumberFormatInfo.InvariantInfo);
                                    if (modifiers.Length > 2) {
                                        c = float.Parse(modifiers[2], System.Globalization.NumberFormatInfo.InvariantInfo);
                                        if (modifiers.Length > 3) {
                                            d = float.Parse(modifiers[3], System.Globalization.NumberFormatInfo.InvariantInfo);
                                        }
                                    }
                                }
                            }
                        }


                        if (PartNamesToAnim.TryGetValue(attrName, out PartNameToAnim toAnim)) {
                            anims.AddRange(toAnim.ToAnim(attrName, a, b, c, d));

                        } else if (attrName.CaselessEq("leftidle")) {
                            anims.Add(new CustomModelAnim {
                                type = CustomModelAnimType.SinRotate,
                                axis = CustomModelAnimAxis.X,
                                a = ANIM_IDLE_XPERIOD,
                                b = ANIM_IDLE_MAX,
                                c = 0,
                                d = 0,
                            });
                            anims.Add(new CustomModelAnim {
                                type = CustomModelAnimType.SinRotate,
                                axis = CustomModelAnimAxis.Z,
                                a = ANIM_IDLE_ZPERIOD,
                                b = -ANIM_IDLE_MAX,
                                c = 0.25f,
                                d = 1,
                            });

                        } else if (attrName.CaselessEq("rightidle")) {
                            anims.Add(new CustomModelAnim {
                                type = CustomModelAnimType.SinRotate,
                                axis = CustomModelAnimAxis.X,
                                a = ANIM_IDLE_XPERIOD,
                                b = -ANIM_IDLE_MAX,
                                c = 0,
                                d = 0,
                            });
                            anims.Add(new CustomModelAnim {
                                type = CustomModelAnimType.SinRotate,
                                axis = CustomModelAnimAxis.Z,
                                a = ANIM_IDLE_ZPERIOD,
                                b = ANIM_IDLE_MAX,
                                c = 0.25f,
                                d = 1,
                            });

                        } else if (attrName.CaselessEq("fullbright")) {
                            part.fullbright = true;
                        } else if (attrName.CaselessEq("hand")) {
                            part.firstPersonArm = true;
                        } else if (attrName.CaselessEq("layer")) {
                            part.layer = true;
                        } else if (attrName.CaselessEq("humanleftarm")) {
                            part.skinLeftArm = true;
                        } else if (attrName.CaselessEq("humanrightarm")) {
                            part.skinRightArm = true;
                        } else if (attrName.CaselessEq("humanleftleg")) {
                            part.skinLeftLeg = true;
                        } else if (attrName.CaselessEq("humanrightleg")) {
                            part.skinRightLeg = true;
                        }
                    }
                    part.anims = anims.ToArray();

                    return part;
                }

                private static readonly Action<CustomModelAnim> FlipValidator = (anim) => {
                    if (anim.d == 0) {
                        throw new Exception("max-value is 0");
                    }
                };
                private static readonly Dictionary<string, PartNameToAnim> PartNamesToAnim =
                    new Dictionary<string, PartNameToAnim>(StringComparer.OrdinalIgnoreCase) {
                    /*
                        a: width
                    */
                    { "head", new PartNameToAnim(CustomModelAnimType.Head, CustomModelAnimAxis.X, 1.0f) },
                    { "headx", new PartNameToAnim(CustomModelAnimType.Head, CustomModelAnimAxis.X, 1.0f) },
                    { "heady", new PartNameToAnim(CustomModelAnimType.Head, CustomModelAnimAxis.Y, 1.0f) },
                    { "headz", new PartNameToAnim(CustomModelAnimType.Head, CustomModelAnimAxis.Z, 1.0f) },

                    { "leftleg", new PartNameToAnim(CustomModelAnimType.LeftLegX, CustomModelAnimAxis.X, 1.0f) },
                    { "leftlegx", new PartNameToAnim(CustomModelAnimType.LeftLegX, CustomModelAnimAxis.X, 1.0f) },
                    { "leftlegy", new PartNameToAnim(CustomModelAnimType.LeftLegX, CustomModelAnimAxis.Y, 1.0f) },
                    { "leftlegz", new PartNameToAnim(CustomModelAnimType.LeftLegX, CustomModelAnimAxis.Z, 1.0f) },

                    { "rightleg", new PartNameToAnim(CustomModelAnimType.RightLegX, CustomModelAnimAxis.X, 1.0f) },
                    { "rightlegx", new PartNameToAnim(CustomModelAnimType.RightLegX, CustomModelAnimAxis.X, 1.0f) },
                    { "rightlegy", new PartNameToAnim(CustomModelAnimType.RightLegX, CustomModelAnimAxis.Y, 1.0f) },
                    { "rightlegz", new PartNameToAnim(CustomModelAnimType.RightLegX, CustomModelAnimAxis.Z, 1.0f) },

                    { "leftarm", new PartNameToAnim(
                        new []{ CustomModelAnimType.LeftArmX, CustomModelAnimType.LeftArmZ },
                        new []{ CustomModelAnimAxis.X, CustomModelAnimAxis.Z},
                        1.0f
                    ) },
                    { "leftarmxx", new PartNameToAnim(CustomModelAnimType.LeftArmX, CustomModelAnimAxis.X, 1.0f) },
                    { "leftarmxy", new PartNameToAnim(CustomModelAnimType.LeftArmX, CustomModelAnimAxis.Y, 1.0f) },
                    { "leftarmxz", new PartNameToAnim(CustomModelAnimType.LeftArmX, CustomModelAnimAxis.Z, 1.0f) },

                    { "rightarm", new PartNameToAnim(
                        new []{ CustomModelAnimType.RightArmX, CustomModelAnimType.RightArmZ },
                        new []{ CustomModelAnimAxis.X, CustomModelAnimAxis.Z},
                        1.0f
                    ) },
                    { "rightarmxx", new PartNameToAnim(CustomModelAnimType.RightArmX, CustomModelAnimAxis.X, 1.0f) },
                    { "rightarmxy", new PartNameToAnim(CustomModelAnimType.RightArmX, CustomModelAnimAxis.Y, 1.0f) },
                    { "rightarmxz", new PartNameToAnim(CustomModelAnimType.RightArmX, CustomModelAnimAxis.Z, 1.0f) },

                    { "leftarmzx", new PartNameToAnim(CustomModelAnimType.LeftArmZ, CustomModelAnimAxis.X, 1.0f) },
                    { "leftarmzy", new PartNameToAnim(CustomModelAnimType.LeftArmZ, CustomModelAnimAxis.Y, 1.0f) },
                    { "leftarmzz", new PartNameToAnim(CustomModelAnimType.LeftArmZ, CustomModelAnimAxis.Z, 1.0f) },

                    { "rightarmzx", new PartNameToAnim(CustomModelAnimType.RightArmZ, CustomModelAnimAxis.X, 1.0f) },
                    { "rightarmzy", new PartNameToAnim(CustomModelAnimType.RightArmZ, CustomModelAnimAxis.Y, 1.0f) },
                    { "rightarmzz", new PartNameToAnim(CustomModelAnimType.RightArmZ, CustomModelAnimAxis.Z, 1.0f) },

                    /*
                        a: speed
                        b: shift pos
                    */
                    { "spinx", new PartNameToAnim(CustomModelAnimType.Spin, CustomModelAnimAxis.X, 1.0f, 0.0f) },
                    { "spiny", new PartNameToAnim(CustomModelAnimType.Spin, CustomModelAnimAxis.Y, 1.0f, 0.0f) },
                    { "spinz", new PartNameToAnim(CustomModelAnimType.Spin, CustomModelAnimAxis.Z, 1.0f, 0.0f) },

                    { "spinxvelocity", new PartNameToAnim(CustomModelAnimType.SpinVelocity, CustomModelAnimAxis.X, 1.0f, 0.0f) },
                    { "spinyvelocity", new PartNameToAnim(CustomModelAnimType.SpinVelocity, CustomModelAnimAxis.Y, 1.0f, 0.0f) },
                    { "spinzvelocity", new PartNameToAnim(CustomModelAnimType.SpinVelocity, CustomModelAnimAxis.Z, 1.0f, 0.0f) },

                    /*
                        a: speed
                        b: width
                        c: shift cycle
                        d: shift pos
                    */

                    // rotate
                    { "sinx", new PartNameToAnim(CustomModelAnimType.SinRotate, CustomModelAnimAxis.X, 1.0f, 1.0f, 0.0f, 0.0f) },
                    { "siny", new PartNameToAnim(CustomModelAnimType.SinRotate, CustomModelAnimAxis.Y, 1.0f, 1.0f, 0.0f, 0.0f) },
                    { "sinz", new PartNameToAnim(CustomModelAnimType.SinRotate, CustomModelAnimAxis.Z, 1.0f, 1.0f, 0.0f, 0.0f) },

                    { "sinxvelocity", new PartNameToAnim(CustomModelAnimType.SinRotateVelocity, CustomModelAnimAxis.X, 1.0f, 1.0f, 0.0f, 0.0f) },
                    { "sinyvelocity", new PartNameToAnim(CustomModelAnimType.SinRotateVelocity, CustomModelAnimAxis.Y, 1.0f, 1.0f, 0.0f, 0.0f) },
                    { "sinzvelocity", new PartNameToAnim(CustomModelAnimType.SinRotateVelocity, CustomModelAnimAxis.Z, 1.0f, 1.0f, 0.0f, 0.0f) },

                    { "cosx", new PartNameToAnim(CustomModelAnimType.SinRotate, CustomModelAnimAxis.X, 1.0f, 1.0f, 0.0f, 0.0f, (anim) => { anim.c += 0.25f; }) },
                    { "cosy", new PartNameToAnim(CustomModelAnimType.SinRotate, CustomModelAnimAxis.Y, 1.0f, 1.0f, 0.0f, 0.0f, (anim) => { anim.c += 0.25f; }) },
                    { "cosz", new PartNameToAnim(CustomModelAnimType.SinRotate, CustomModelAnimAxis.Z, 1.0f, 1.0f, 0.0f, 0.0f, (anim) => { anim.c += 0.25f; }) },

                    { "cosxvelocity", new PartNameToAnim(CustomModelAnimType.SinRotateVelocity, CustomModelAnimAxis.X, 1.0f, 1.0f, 0.0f, 0.0f, (anim) => { anim.c += 0.25f; }) },
                    { "cosyvelocity", new PartNameToAnim(CustomModelAnimType.SinRotateVelocity, CustomModelAnimAxis.Y, 1.0f, 1.0f, 0.0f, 0.0f, (anim) => { anim.c += 0.25f; }) },
                    { "coszvelocity", new PartNameToAnim(CustomModelAnimType.SinRotateVelocity, CustomModelAnimAxis.Z, 1.0f, 1.0f, 0.0f, 0.0f, (anim) => { anim.c += 0.25f; }) },

                    // translate
                    { "pistonx", new PartNameToAnim(CustomModelAnimType.SinTranslate, CustomModelAnimAxis.X, 1.0f, 1.0f, 0.0f, 0.0f) },
                    { "pistony", new PartNameToAnim(CustomModelAnimType.SinTranslate, CustomModelAnimAxis.Y, 1.0f, 1.0f, 0.0f, 0.0f) },
                    { "pistonz", new PartNameToAnim(CustomModelAnimType.SinTranslate, CustomModelAnimAxis.Z, 1.0f, 1.0f, 0.0f, 0.0f) },

                    { "pistonxvelocity", new PartNameToAnim(CustomModelAnimType.SinTranslateVelocity, CustomModelAnimAxis.X, 1.0f, 1.0f, 0.0f, 0.0f) },
                    { "pistonyvelocity", new PartNameToAnim(CustomModelAnimType.SinTranslateVelocity, CustomModelAnimAxis.Y, 1.0f, 1.0f, 0.0f, 0.0f) },
                    { "pistonzvelocity", new PartNameToAnim(CustomModelAnimType.SinTranslateVelocity, CustomModelAnimAxis.Z, 1.0f, 1.0f, 0.0f, 0.0f) },

                    // size
                    { "pulsate", new PartNameToAnim(
                        new []{ CustomModelAnimType.SinSize, CustomModelAnimType.SinSize, CustomModelAnimType.SinSize },
                        new []{ CustomModelAnimAxis.X, CustomModelAnimAxis.Y, CustomModelAnimAxis.Z},
                        1.0f, 1.0f, 0.0f, 0.0f
                    ) },
                    { "pulsatex", new PartNameToAnim(CustomModelAnimType.SinSize, CustomModelAnimAxis.X, 1.0f, 1.0f, 0.0f, 0.0f) },
                    { "pulsatey", new PartNameToAnim(CustomModelAnimType.SinSize, CustomModelAnimAxis.Y, 1.0f, 1.0f, 0.0f, 0.0f) },
                    { "pulsatez", new PartNameToAnim(CustomModelAnimType.SinSize, CustomModelAnimAxis.Z, 1.0f, 1.0f, 0.0f, 0.0f) },

                    { "pulsatevelocity", new PartNameToAnim(
                        new []{ CustomModelAnimType.SinSizeVelocity, CustomModelAnimType.SinSizeVelocity, CustomModelAnimType.SinSizeVelocity },
                        new []{ CustomModelAnimAxis.X, CustomModelAnimAxis.Y, CustomModelAnimAxis.Z},
                        1.0f, 1.0f, 0.0f, 0.0f
                    ) },
                    { "pulsatexvelocity", new PartNameToAnim(CustomModelAnimType.SinSizeVelocity, CustomModelAnimAxis.X, 1.0f, 1.0f, 0.0f, 0.0f) },
                    { "pulsateyvelocity", new PartNameToAnim(CustomModelAnimType.SinSizeVelocity, CustomModelAnimAxis.Y, 1.0f, 1.0f, 0.0f, 0.0f) },
                    { "pulsatezvelocity", new PartNameToAnim(CustomModelAnimType.SinSizeVelocity, CustomModelAnimAxis.Z, 1.0f, 1.0f, 0.0f, 0.0f) },


                    /*
                        a: speed
                        b: width
                        c: shift cycle
                        d: max value
                    */
                    // rotate
                    { "flipx", new PartNameToAnim(CustomModelAnimType.FlipRotate, CustomModelAnimAxis.X, 1.0f, 1.0f, 0.0f, 1.0f, FlipValidator) },
                    { "flipy", new PartNameToAnim(CustomModelAnimType.FlipRotate, CustomModelAnimAxis.Y, 1.0f, 1.0f, 0.0f, 1.0f, FlipValidator) },
                    { "flipz", new PartNameToAnim(CustomModelAnimType.FlipRotate, CustomModelAnimAxis.Z, 1.0f, 1.0f, 0.0f, 1.0f, FlipValidator) },

                    { "flipxvelocity", new PartNameToAnim(CustomModelAnimType.FlipRotateVelocity, CustomModelAnimAxis.X, 1.0f, 1.0f, 0.0f, 1.0f, FlipValidator) },
                    { "flipyvelocity", new PartNameToAnim(CustomModelAnimType.FlipRotateVelocity, CustomModelAnimAxis.Y, 1.0f, 1.0f, 0.0f, 1.0f, FlipValidator) },
                    { "flipzvelocity", new PartNameToAnim(CustomModelAnimType.FlipRotateVelocity, CustomModelAnimAxis.Z, 1.0f, 1.0f, 0.0f, 1.0f, FlipValidator) },

                    // translate
                    { "fliptranslatex", new PartNameToAnim(CustomModelAnimType.FlipTranslate, CustomModelAnimAxis.X, 1.0f, 1.0f, 0.0f, 1.0f, FlipValidator) },
                    { "fliptranslatey", new PartNameToAnim(CustomModelAnimType.FlipTranslate, CustomModelAnimAxis.Y, 1.0f, 1.0f, 0.0f, 1.0f, FlipValidator) },
                    { "fliptranslatez", new PartNameToAnim(CustomModelAnimType.FlipTranslate, CustomModelAnimAxis.Z, 1.0f, 1.0f, 0.0f, 1.0f, FlipValidator) },

                    { "fliptranslatexvelocity", new PartNameToAnim(CustomModelAnimType.FlipTranslateVelocity, CustomModelAnimAxis.X, 1.0f, 1.0f, 0.0f, 1.0f, FlipValidator) },
                    { "fliptranslateyvelocity", new PartNameToAnim(CustomModelAnimType.FlipTranslateVelocity, CustomModelAnimAxis.Y, 1.0f, 1.0f, 0.0f, 1.0f, FlipValidator) },
                    { "fliptranslatezvelocity", new PartNameToAnim(CustomModelAnimType.FlipTranslateVelocity, CustomModelAnimAxis.Z, 1.0f, 1.0f, 0.0f, 1.0f, FlipValidator) },

                    // size
                    { "flipsizex", new PartNameToAnim(CustomModelAnimType.FlipSize, CustomModelAnimAxis.X, 1.0f, 1.0f, 0.0f, 1.0f, FlipValidator) },
                    { "flipsizey", new PartNameToAnim(CustomModelAnimType.FlipSize, CustomModelAnimAxis.Y, 1.0f, 1.0f, 0.0f, 1.0f, FlipValidator) },
                    { "flipsizez", new PartNameToAnim(CustomModelAnimType.FlipSize, CustomModelAnimAxis.Z, 1.0f, 1.0f, 0.0f, 1.0f, FlipValidator) },

                    { "flipsizexvelocity", new PartNameToAnim(CustomModelAnimType.FlipSizeVelocity, CustomModelAnimAxis.X, 1.0f, 1.0f, 0.0f, 1.0f, FlipValidator) },
                    { "flipsizeyvelocity", new PartNameToAnim(CustomModelAnimType.FlipSizeVelocity, CustomModelAnimAxis.Y, 1.0f, 1.0f, 0.0f, 1.0f, FlipValidator) },
                    { "flipsizezvelocity", new PartNameToAnim(CustomModelAnimType.FlipSizeVelocity, CustomModelAnimAxis.Z, 1.0f, 1.0f, 0.0f, 1.0f, FlipValidator) },
                };

                class PartNameToAnim {
                    private readonly CustomModelAnimType[] types;
                    private readonly CustomModelAnimAxis[] axes;
                    private readonly float defaultA;
                    private readonly float defaultB;
                    private readonly float defaultC;
                    private readonly float defaultD;
                    private readonly Action<CustomModelAnim> validator;

                    public PartNameToAnim(
                        CustomModelAnimType[] types,
                        CustomModelAnimAxis[] axes,
                        float defaultA = 1.0f,
                        float defaultB = 1.0f,
                        float defaultC = 1.0f,
                        float defaultD = 1.0f,
                        Action<CustomModelAnim> validator = null
                    ) {
                        this.types = types;
                        this.axes = axes;
                        this.defaultA = defaultA;
                        this.defaultB = defaultB;
                        this.defaultC = defaultC;
                        this.defaultD = defaultD;
                        this.validator = validator;
                    }

                    public PartNameToAnim(
                        CustomModelAnimType type,
                        CustomModelAnimAxis axis,
                        float defaultA = 1.0f,
                        float defaultB = 1.0f,
                        float defaultC = 1.0f,
                        float defaultD = 1.0f,
                        Action<CustomModelAnim> validator = null
                    ) : this(
                        new[] { type },
                        new[] { axis },
                        defaultA,
                        defaultB,
                        defaultC,
                        defaultD,
                        validator
                    ) { }

                    public CustomModelAnim[] ToAnim(
                        string attrName,
                        float? a = null,
                        float? b = null,
                        float? c = null,
                        float? d = null
                    ) {
                        var anims = new List<CustomModelAnim>();
                        for (int i = 0; i < types.Length; i++) {
                            var type = types[i];
                            var axis = axes[i];

                            var anim = new CustomModelAnim {
                                type = type,
                                axis = axis,
                                a = a ?? this.defaultA,
                                b = b ?? this.defaultB,
                                c = c ?? this.defaultC,
                                d = d ?? this.defaultD,
                            };
                            if (this.validator != null) {
                                try {
                                    this.validator.Invoke(anim);
                                } catch (Exception e) {
                                    throw new Exception(
                                        string.Format(
                                            "Couldn't validate {0}: {1}",
                                            attrName,
                                            e.Message
                                        )
                                    );
                                }
                            }

                            anims.Add(anim);
                        }
                        return anims.ToArray();
                    }
                }

                private const float MATH_PI = 3.1415926535897931f;
                private const float MATH_DEG2RAD = MATH_PI / 180.0f;
                // private const float ANIM_MAX_ANGLE = 110 * MATH_DEG2RAD;
                // private const float ANIM_ARM_MAX = 60.0f * MATH_DEG2RAD;
                // private const float ANIM_LEG_MAX = 80.0f * MATH_DEG2RAD;
                private const float ANIM_IDLE_MAX = 3.0f * MATH_DEG2RAD;
                private const float ANIM_IDLE_XPERIOD = 2.0f * MATH_PI / 5.0f;
                private const float ANIM_IDLE_ZPERIOD = 2.0f * MATH_PI / 3.5f;


                public class Resolution {
                    public UInt16 width;
                    public UInt16 height;
                }
                public class Meta {
                    public string model_format;
                }
                public class Element {
                    public Element() {
                        this.rotation = new[] { 0.0f, 0.0f, 0.0f };
                        this.origin = new[] { 0.0f, 0.0f, 0.0f };
                        this.visibility = true;
                        this.inflate = 0.0f;
                    }

                    public string name;
                    // 3 numbers
                    public float[] from;
                    // 3 numbers
                    public float[] to;

                    public bool visibility;

                    // if set to 1, uses a default png with some colors on it,
                    // we will only support skin pngs, so maybe notify user?
                    public UInt16 autouv;

                    public float inflate;

                    // if false, mirroring is enabled
                    // if null, mirroring is disabled
                    public bool? shade;

                    // 3 numbers
                    public float[] rotation;

                    /// "Pivot Point"
                    // 3 numbers
                    public float[] origin;

                    public Faces faces;
                    public string uuid;
                }
                public class Faces {
                    public Face north;
                    public Face east;
                    public Face south;
                    public Face west;
                    public Face up;
                    public Face down;
                }
                public class Face {
                    public Face() {
                        this.rotation = 0;
                    }

                    // 4 numbers
                    public float[] uv;
                    public string texture = null;
                    public float rotation = 0;
                }
                public class UuidOrGroup {
                    public string uuid;
                    public OutlinerGroup group;
                }
                public class OutlinerGroup {
                    public OutlinerGroup() {
                        this.rotation = new[] { 0.0f, 0.0f, 0.0f };
                        this.origin = new[] { 0.0f, 0.0f, 0.0f };
                        this.visibility = true;
                    }
                    public string name;
                    public string uuid;

                    public bool visibility;

                    // 3 numbers
                    public float[] rotation;

                    /// "Pivot Point"
                    // 3 numbers
                    public float[] origin;

                    public UuidOrGroup[] children;
                }
                public class JsonUuidOrGroup : JsonConverter {
                    public override bool CanConvert(Type objectType) {
                        return objectType == typeof(UuidOrGroup);
                    }

                    public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer) {
                        if (reader.TokenType == JsonToken.String) {
                            JValue jValue = new JValue(reader.Value);
                            return new UuidOrGroup {
                                uuid = (string)jValue,
                                group = null,
                            };
                        } else {
                            JObject jo = JObject.Load(reader);
                            var group = new OutlinerGroup { };
                            serializer.Populate(jo.CreateReader(), group);
                            return new UuidOrGroup {
                                uuid = null,
                                group = group,
                            };
                        }
                    }

                    public override bool CanWrite => false;
                    public override void WriteJson(JsonWriter writer,
                        object value, JsonSerializer serializer) {
                        throw new NotImplementedException();
                    }
                }

            }

            private static readonly JsonSerializerSettings jsonSettings = new JsonSerializerSettings {
                Converters = new[] { new JsonRoot.JsonUuidOrGroup() }
            };

            public static JsonRoot Parse(string json) {
                JsonRoot m = JsonConvert.DeserializeObject<JsonRoot>(json, jsonSettings);
                return m;
            }
        } // class BlockBench
#pragma warning restore 0649

    } // class CustomModelsPlugin
} // namespace MCGalaxy
