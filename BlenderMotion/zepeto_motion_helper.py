bl_info = {
    "name": "ZEPETO 모션 헬퍼",
    "author": "easy",
    "version": (1, 4, 0),
    "blender": (4, 2, 0),
    "location": "3D View > 사이드바(N) > ZEPETO",
    "description": "ZEPETO 의상 미리보기용 모션을 버튼 몇 개로 만들고 Unity로 내보냅니다.",
    "category": "Animation",
}

import os
import re
import time
from collections import namedtuple
import bpy
from bpy.props import BoolProperty, StringProperty
from mathutils import Euler, Vector

FPS = 24
DEFAULT_END = 48
RIG_NAME = "HumanoidRig"

# The 55 bones Unity's Humanoid avatar actually maps, read out of ZepetoBaseModel.fbx.meta (humanDescription).
# A Humanoid AnimationClip stores ONLY these; rotation authored on any other bone is silently discarded at
# import. That is 49 of the rig's 103 bones - every *Twist*, every *_scale*, pelvis, heel and most of the
# face - so a beginner who picks the wrong bone sees their work simply vanish in Unity with no error.
#
# Note "hips": in the fbx it is the armature ROOT, which Blender turns into the armature OBJECT rather than a
# bone. So the Humanoid Hips cannot be posed as a bone here at all - moving the object is what sends the
# avatar walking out of frame. It is listed for completeness; is_mapped_bone never sees it.
MAPPED_BONES = frozenset((
    "hips", "spine", "chest", "chestUpper", "neck", "head",
    "shoulder_L", "upperArm_L", "lowerArm_L", "hand_L",
    "shoulder_R", "upperArm_R", "lowerArm_R", "hand_R",
    "upperLeg_L", "lowerLeg_L", "foot_L", "toes_L",
    "upperReg_R", "lowerReg_R", "foot_R", "toes_R",
    "eye_L", "eye_R", "mouth",
    "thumbPro_L", "thumbInt_L", "thumbDis_L",
    "indexPro_L", "indexInt_L", "indexDis_L",
    "middlePro_L", "middleInt_L", "middleDis_L",
    "ringPro_L", "ringInt_L", "ringDis_L",
    "littlePro_L", "littleInt_L", "littleDis_L",
    "thumbPro_R", "thumbInt_R", "thumbDis_R",
    "indexPro_R", "indexInt_R", "indexDis_R",
    "middlePro_R", "middleInt_R", "middleDis_R",
    "ringPro_R", "ringInt_R", "ringDis_R",
    "littlePro_R", "littleInt_R", "littleDis_R",
))



# ---------------------------------------------------------------- Unity project location
#
# Both paths this add-on needs live inside the Unity project, and that project sits somewhere different on
# every machine. They used to be module-level constants baked into this file, which shipped one developer's
# C:\Users\<name>\... path to everyone else: Blender evaluates a property's `default=` once at registration,
# so a new .blend or a new scene inherited a folder that does not exist, and both "몸 불러오기" and
# "Unity로 보내기" failed on a machine that was otherwise set up correctly. Only the one live .blend worked,
# because its saved scene properties happened to hold real values.
#
# So resolve at run time instead, mirroring what the Unity side already does in
# Packages/com.easy.zepeto-helper/Editor/ZepetoStudioHelperWindow.GoToBlender.cs (GuessBlendFilePath): walk up
# from a known starting point looking for the project next to it. Pure stdlib, so nothing here depends on the
# Blender version.
UNITY_PROJECT_ENV = "ZEPETO_UNITY_PROJECT"
UNITY_PROJECT_PREFIX = "zepeto studio unity project file"     # compared lower-cased
PROJECT_WALK_LIMIT = 6
# How much newer one candidate's Assets folder has to be before age alone is allowed to decide between two
# otherwise equally plausible projects (seconds). A project someone is actually working in gets touched the
# same day; a leftover download sitting beside it does not. Anything closer than this counts as a tie, and a
# tie is reported to the user, never guessed - see _pick_project.
PROJECT_AGE_MARGIN = 24 * 60 * 60

# What refresh_paths did, as opposed to what it found.
#   project     the folder that resolved, or "" - NOT a claim that anything was fixed
#   export_dir  the value actually written to scene.zepeto_export_dir, or ""
#   rig_fbx     the value actually written to scene.zepeto_rig_fbx, or ""
#   ambiguous   projects the search found but refused to choose between (tuple, usually empty)
PathFix = namedtuple("PathFix", "project export_dir rig_fbx ambiguous")


def _looks_like_unity_project(path):
    """Named like the ZEPETO Studio download AND actually carrying an Assets folder."""
    if not path or not os.path.isdir(path):
        return False
    name = os.path.basename(os.path.normpath(path)).lower()
    return name.startswith(UNITY_PROJECT_PREFIX) and os.path.isdir(os.path.join(path, "Assets"))


def _anchor_dirs():
    """
    Folders that prove which project the user is in: the open .blend, and this add-on file.

    Kept separate from resolve_unity_project's start list on purpose - a start point is a place to search FROM,
    an anchor is evidence ABOUT a candidate.
    """
    out = []
    if bpy.data.filepath:
        out.append(os.path.dirname(os.path.abspath(bpy.data.filepath)))
    # globals().get: run from Blender's Text editor rather than as an installed add-on, __file__ can be absent,
    # and a NameError here would surface as the raw traceback this whole file works to avoid.
    own_file = globals().get("__file__", "")
    if own_file:
        out.append(os.path.dirname(os.path.abspath(own_file)))
    return out


def _is_inside(parent, child):
    """True when child is parent or lives under it. Pure path math, so a folder that is gone is just False."""
    try:
        parent = os.path.normcase(os.path.abspath(parent))
        child = os.path.normcase(os.path.abspath(child))
    except (OSError, ValueError):
        return False
    return child == parent or child.startswith(parent + os.sep)


def _rank_project(path):
    """
    How sure we can be that `path` is the project the user means. Higher wins; an equal rank is a tie.

      2  it contains the open .blend or this add-on file - nothing beats holding the file we are running from
      1  it carries BOTH folders this pipeline uses (Assets/CustomMotions and Assets/ZepetoHelper/Rig), so the
         helper has actually been used with it, unlike a second copy of the download
      0  it merely looks like a Unity project
    """
    for anchor in _anchor_dirs():
        if _is_inside(path, anchor):
            return 2
    # Both folders, not either: Assets/CustomMotions is the live-preview watch root and
    # Assets/ZepetoHelper/Rig is where the Unity side writes the rig. They are deliberately different folders.
    if (os.path.isdir(os.path.join(path, "Assets", "CustomMotions"))
            and os.path.isdir(os.path.join(path, "Assets", "ZepetoHelper", "Rig"))):
        return 1
    return 0


def _assets_mtime(path):
    """When this project's Assets folder was last touched. 0.0 when it cannot be read, so it never wins a tie."""
    try:
        return os.path.getmtime(os.path.join(path, "Assets"))
    except OSError:
        return 0.0


def _pick_project(candidates):
    """
    Choose between projects sitting side by side, or refuse to. Returns (project, ambiguous).

    Exactly one half is filled: a folder and (), or "" and the candidates that tied.

    This used to be "whatever os.listdir sorted first", which is how a stale "... 3.2.12" download or a "(1)"
    duplicate beside the real project could silently win. The export then lands in a project Unity is not
    watching, and the user sees a successful export that never shows up - the hardest failure here to diagnose.
    So: rank on evidence, and when the evidence ties, say so instead of picking.
    """
    unique = []
    for path in candidates:
        norm = os.path.normpath(path)
        if norm not in unique:
            unique.append(norm)
    if not unique:
        return "", ()
    if len(unique) == 1:
        return unique[0], ()

    ranked = [(_rank_project(p), p) for p in unique]
    best = max(rank for rank, _ in ranked)
    top = [p for rank, p in ranked if rank == best]
    if len(top) == 1:
        return top[0], ()

    # Last resort, and only when it is not a close call (PROJECT_AGE_MARGIN).
    top.sort(key=_assets_mtime, reverse=True)
    if _assets_mtime(top[0]) - _assets_mtime(top[1]) >= PROJECT_AGE_MARGIN:
        return top[0], ()
    return "", tuple(sorted(top))


def _walk_up_for_project(start_dir):
    """
    Test start_dir, then every ancestor, and also each ancestor's immediate children.

    Looking at the children is what makes the shipped layout work: the .blend lives in a BlenderMotion folder
    *beside* the project, not inside it. The walk is capped so one button press can never turn into a crawl of
    a whole drive.

    Returns (project, ambiguous), same as _pick_project. The nearest level holding any candidate at all decides:
    proximity outranks every other signal, and stopping there keeps the ambiguous set down to the folders
    actually sitting next to each other.
    """
    current = start_dir
    for _ in range(PROJECT_WALK_LIMIT + 1):
        if not current or not os.path.isdir(current):
            return "", ()
        if _looks_like_unity_project(current):
            # start_dir is this folder or lives under it, so it contains us - unambiguous by definition.
            return os.path.normpath(current), ()
        try:
            entries = sorted(os.listdir(current))
        except OSError:
            entries = []
        matches = [os.path.join(current, entry) for entry in entries
                   if _looks_like_unity_project(os.path.join(current, entry))]
        if matches:
            return _pick_project(matches)
        parent = os.path.dirname(current)
        if parent == current:                                  # reached the drive root
            return "", ()
        current = parent
    return "", ()


def resolve_unity_project():
    """
    The Unity project folder on THIS machine, plus any ambiguity we refused to resolve.

    Returns (project, ambiguous): a folder and (), or "" and the candidate projects that tied.

    Order: the env var first, so a user can always force an answer; then the folder of the open .blend; then
    this add-on file's own folder, which still resolves when the .blend was never saved.
    """
    forced = os.environ.get(UNITY_PROJECT_ENV, "").strip().strip('"')
    if forced and os.path.isdir(os.path.join(forced, "Assets")):
        return os.path.normpath(forced), ()

    starts = []
    if bpy.data.filepath:
        starts.append(os.path.dirname(bpy.data.filepath))
    own_file = globals().get("__file__", "")
    if own_file:
        starts.append(os.path.dirname(os.path.abspath(own_file)))
    ambiguous = ()
    for start in starts:
        found, tied = _walk_up_for_project(start)
        if found:
            return found, ()
        # Remember the first tie but keep going: the add-on's own folder may still give a clean answer.
        if tied and not ambiguous:
            ambiguous = tied
    return "", ambiguous


def refresh_paths(scene, force=False):
    """
    Fill the scene's two path properties from resolve_unity_project(). Returns a PathFix.

    The return value reports what was WRITTEN, not what was found. Callers must report from that: finding a
    project folder that turns out to be missing both subfolders used to be announced to the user as a
    successful repair, so the one repair button in the UI claimed to have fixed a state that was still broken.

    Called from operators only, never from draw(): writing to ID data during a panel draw is not allowed, and
    a directory walk has no business running on every redraw. Saving the .blend to a new location therefore
    fixes the paths on the next button press, with no code edit.

    A value that still exists is left alone unless force is set, so a folder the user picked by hand is never
    clobbered. When nothing resolves the property is left EMPTY on purpose: an empty field asks the user for a
    folder, while a wrong path fails later and looks like the tool itself is broken.
    """
    if scene is None:
        return PathFix("", "", "", ())
    export_dir = bpy.path.abspath(scene.zepeto_export_dir)
    rig_fbx = bpy.path.abspath(scene.zepeto_rig_fbx)
    need_dir = force or not export_dir or not os.path.isdir(export_dir)
    need_rig = force or not rig_fbx or not os.path.isfile(rig_fbx)
    if not need_dir and not need_rig:
        return PathFix("", "", "", ())

    project, ambiguous = resolve_unity_project()
    if not project:
        return PathFix("", "", "", ambiguous)
    set_dir = ""
    set_rig = ""
    if need_dir:
        # Assets/CustomMotions is the folder Unity polls for live preview; keep it distinct from the two
        # ZepetoHelper folders the Unity side owns.
        candidate = os.path.join(project, "Assets", "CustomMotions")
        if os.path.isdir(candidate):
            scene.zepeto_export_dir = candidate
            set_dir = candidate
    if need_rig:
        candidate = os.path.join(project, "Assets", "ZepetoHelper", "Rig", "ZepetoBaseModel.fbx")
        if os.path.isfile(candidate):
            scene.zepeto_rig_fbx = candidate
            set_rig = candidate
    return PathFix(project, set_dir, set_rig, ambiguous)


def export_dir_problem(scene):
    """The panel and the export operator must agree on what a usable save folder is, so both ask here."""
    folder = bpy.path.abspath(scene.zepeto_export_dir)
    if not folder:
        return "저장 폴더가 비어 있습니다. 패널의 '저장 폴더'를 직접 지정하세요 (Unity 프로젝트의 Assets/CustomMotions)"
    if not os.path.isdir(folder):
        return "저장 폴더가 없습니다: %s - 패널의 '저장 폴더'를 직접 지정하세요" % folder
    return None


# ---------------------------------------------------------------- helpers
def get_rig(context):
    """Any armature works: the practice rig, or the real ZEPETO model exported out of Unity."""
    obj = getattr(context, "object", None)
    if obj and obj.type == "ARMATURE":
        return obj
    named = bpy.data.objects.get(RIG_NAME)
    if named:
        return named
    for candidate in bpy.data.objects:
        if candidate.type == "ARMATURE":
            return candidate
    return None




def is_mapped_bone(name):
    return name in MAPPED_BONES


def apply_bone_visibility(rig, show_all):
    """
    Hide the bones Unity throws away, so a click can only land on one that works.

    Returns how many were hidden. Hiding rather than warning afterwards is deliberate: the failure is
    invisible (no error, no warning - the motion just does not move that joint in Unity), so the only
    reliable fix is to make the mistake unreachable.
    """
    if rig is None:
        return 0
    hidden = 0
    for bone in rig.data.bones:
        dead = not is_mapped_bone(bone.name)
        bone.hide = dead and not show_all
        if bone.hide:
            hidden += 1
    return hidden


def _on_show_all_bones_changed(self, context):
    apply_bone_visibility(get_rig(context), self.zepeto_show_all_bones)


def _has_quat_curves(rig, name):
    action = rig.animation_data.action if rig and rig.animation_data else None
    prefix = 'pose.bones["%s"].rotation_quaternion' % name
    return any((fc.data_path or "").startswith(prefix) for fc in iter_fcurves(action))


def ensure_euler(rig):
    """
    Bones this tool keys must be in XYZ euler, because every operator here writes rotation_euler.

    Only the import button used to set the mode. An appended or linked rig keeps QUATERNION, Blender still
    stores the euler values and inserts euler F-curves, evaluation ignores them completely - and the panel's
    checklist counts those dead curves and reports "보낼 준비 완료". The result is a motionless clip in Unity
    with no message anywhere.

    A bone that already carries quaternion F-curves is left alone and reported: flipping its mode orphans
    those curves and destroys that animation silently, which is worse than the bug.
    """
    blocked = []
    if rig is None:
        return blocked
    for pb in rig.pose.bones:
        if pb.rotation_mode == "XYZ":
            continue
        if _has_quat_curves(rig, pb.name):
            blocked.append(pb.name)
            continue
        pb.rotation_mode = "XYZ"
    return blocked


def mapped_coverage(rig):
    """How many of the Humanoid-mapped names this rig actually has. 54 is the ceiling - 'hips' is the
    armature object, never a bone."""
    if rig is None:
        return 0
    return len(MAPPED_BONES & {b.name for b in rig.data.bones})


# Windows rejects these outright and bpy.ops.export_scene.fbx surfaces that as a raw traceback rather than
# anything readable, so the name is checked before the button is ever enabled.
BAD_NAME_CHARS = r'\/:*?"<>|'


def motion_name_problem(raw):
    name = (raw or "").strip()
    if not name:
        return "이름 칸이 비어 있습니다"
    bad = sorted({c for c in name if c in BAD_NAME_CHARS})
    if bad:
        return "이름에 쓸 수 없는 문자가 있습니다: %s" % " ".join(bad)
    if name.rstrip(". ") != name:
        return "이름이 마침표나 공백으로 끝날 수 없습니다"
    return None


def keyed_dead_bones(rig):
    """Mapped-bone check for keyframes that already exist, in case the user unhid the rest."""
    action = rig.animation_data.action if rig and rig.animation_data else None
    out = set()
    for fc in iter_fcurves(action):
        path = fc.data_path or ""
        if not path.startswith('pose.bones["'):
            continue
        name = path.split('"')[1]
        if not is_mapped_bone(name):
            out.add(name)
    return sorted(out)


def iter_fcurves(action):
    """Blender 4.4+ moved fcurves into action layers/slots; support both shapes."""
    if action is None:
        return []
    if hasattr(action, "fcurves"):
        return list(action.fcurves)
    curves = []
    for layer in action.layers:
        for strip in layer.strips:
            for bag in getattr(strip, "channelbags", []):
                curves.extend(bag.fcurves)
    return curves


def frame_character(context, rig=None):
    """
    Aim the viewport at the character: front, orthographic, filling the view.

    The view is set by writing region_3d directly rather than calling view3d.view_all / view_selected.
    Those operators behave differently depending on mode and selection - in Pose Mode view_selected frames
    only selected bones (nothing selected => a 10 cm zoom), and view_all would include the default cube,
    camera and light. Computing the bounds ourselves is the only version that lands the same way every time.
    """
    try:
        # Only the character's own meshes. Including every mesh in the scene drags Blender's default cube at
        # the origin into the bounds, and since the imported rig sits ~53 m away the view ends up centred on
        # empty space between the two.
        rig = rig or get_rig(context)
        meshes = [o for o in bpy.data.objects
                  if o.type == "MESH" and (rig is None or o.parent == rig or o == rig)]
        if not meshes:
            meshes = [o for o in bpy.data.objects if o.type == "MESH"]

        lo = Vector((1e9, 1e9, 1e9))
        hi = Vector((-1e9, -1e9, -1e9))
        found = False
        for obj in meshes:
            for corner in obj.bound_box:
                p = obj.matrix_world @ Vector(corner)
                lo = Vector((min(lo.x, p.x), min(lo.y, p.y), min(lo.z, p.z)))
                hi = Vector((max(hi.x, p.x), max(hi.y, p.y), max(hi.z, p.z)))
                found = True
        if not found:
            return False

        center = (lo + hi) * 0.5
        height = max((hi - lo).z, (hi - lo).x, 0.1)

        for window in context.window_manager.windows:
            for area in window.screen.areas:
                if area.type != "VIEW_3D":
                    continue
                space = area.spaces.active
                space.show_region_ui = True
                space.shading.type = "SOLID"
                space.clip_start = 0.01
                space.clip_end = max(1000.0, center.length * 4.0)

                r3d = space.region_3d
                r3d.view_perspective = "ORTHO"
                # Front view: rotate the default top-down view 90 degrees about X.
                r3d.view_rotation = Euler((1.5707963, 0.0, 0.0), "XYZ").to_quaternion()
                r3d.view_location = center
                r3d.view_distance = height * 2.6
                area.tag_redraw()

        frame_timeline(context)
        return True
    except Exception:
        return False


def frame_timeline(context):
    """
    Zoom the timeline to the 2 seconds the motion actually occupies.

    Blender's default view runs out past frame 250, so a beginner sees their 48 frames squeezed into the
    left fifth of the strip and cannot tell one keyframe from the next.
    """
    for window in context.window_manager.windows:
        for area in window.screen.areas:
            if area.type not in {"DOPESHEET_EDITOR", "TIMELINE"}:
                continue
            region = next((r for r in area.regions if r.type == "WINDOW"), None)
            if region is None:
                continue
            try:
                with context.temp_override(window=window, area=area, region=region):
                    bpy.ops.action.view_all()
            except Exception:
                pass
            area.tag_redraw()

def odd_bones(rig):
    """Bones whose pose transform is not identity, ignoring rotation (rotation is the point of the tool)."""
    out = set()
    if rig is None:
        return out
    for pb in rig.pose.bones:
        if pb.location.length > 0.01 or (pb.scale - Vector((1, 1, 1))).length > 0.05:
            out.add(pb.name)
    return out


def keyframe_times(rig):
    action = rig.animation_data.action if rig and rig.animation_data else None
    times = set()
    for fc in iter_fcurves(action):
        for kp in fc.keyframe_points:
            times.add(round(kp.co[0]))
    return sorted(times)




# ---------------------------------------------------------------- operators
class ZEPETO_OT_import_rig(bpy.types.Operator):
    bl_idname = "zepeto.import_rig"
    bl_label = "ZEPETO 리그 불러오기"
    bl_description = "Unity에서 내보낸 ZEPETO 실제 모델 FBX를 불러옵니다. 체형이 정확히 맞습니다"
    bl_options = {"REGISTER", "UNDO"}

    def execute(self, context):
        # Resolve before validating: the stored path may be empty (fresh scene) or may have come from another
        # machine with the .blend. refresh_paths only overwrites what is missing.
        refresh_paths(context.scene)
        path = bpy.path.abspath(context.scene.zepeto_rig_fbx)
        if not path or not os.path.isfile(path):
            self.report({"ERROR"},
                        "리그 FBX를 찾을 수 없습니다. Unity 헬퍼에서 'ZEPETO 리그 내보내기'를 먼저 누르세요. "
                        "이미 내보냈다면 아래 'ZEPETO FBX' 칸에 그 파일을 직접 지정하세요")
            return {"CANCELLED"}

        active = getattr(context, "object", None)
        if active and active.mode != "OBJECT":
            bpy.ops.object.mode_set(mode="OBJECT")

        before = set(bpy.data.objects.keys())
        bpy.ops.import_scene.fbx(filepath=path, automatic_bone_orientation=False)
        added = [bpy.data.objects[n] for n in bpy.data.objects.keys() if n not in before]

        rig = next((o for o in added if o.type == "ARMATURE"), None)
        if rig is None:
            self.report({"ERROR"}, "FBX 안에서 뼈대를 찾지 못했습니다")
            return {"CANCELLED"}

        scene = context.scene
        scene.render.fps = FPS
        scene.frame_start = 1
        scene.frame_current = 1

        # The rig fbx carries no animation, so Blender leaves the default 250 frame timeline in place.
        # Left alone, "반복되게 만들기" would key the loop at frame 250 and produce a 10 second clip.
        # Snap back to the 2 second working length unless the file actually brought a longer action.
        imported_end = 0
        for obj in added:
            if obj.animation_data and obj.animation_data.action:
                try:
                    imported_end = max(imported_end, int(obj.animation_data.action.frame_range[1]))
                except (AttributeError, TypeError):
                    pass
        scene.frame_end = imported_end if imported_end > 1 else DEFAULT_END

        rig.show_in_front = True                 # bones drawn over the body
        rig.data.display_type = "OCTAHEDRAL"
        for obj in added:
            if obj.type == "MESH":
                obj.hide_select = True           # clicks always land on a bone, never the body

        context.view_layer.objects.active = rig
        rig.select_set(True)
        bpy.ops.object.mode_set(mode="POSE")
        for pb in rig.pose.bones:
            pb.rotation_mode = "XYZ"

        # Record which bones already deviate from rest right after import. The ZEPETO rig ships with a few
        # non-unit pose scales, and without a baseline the checklist accuses the user of breaking them.
        scene.zepeto_baseline_odd = ",".join(sorted(odd_bones(rig)))

        hidden = apply_bone_visibility(rig, scene.zepeto_show_all_bones)
        frame_character(context, rig)

        # Count from the mapping, not len(bones) - hidden. With '무시하는 뼈도 보기' on, hidden is 0 and the
        # subtraction claimed all 103 bones were usable - the exact opposite of the truth, at the exact moment
        # the user had opted into seeing the dead ones.
        usable = mapped_coverage(rig)
        dead = len(rig.data.bones) - usable
        tail = ("Unity가 무시하는 %d개는 숨김" % dead) if hidden else                ("Unity가 무시하는 %d개도 보이는 중 - 돌려도 Unity에서 사라집니다" % dead)
        self.report({"INFO"},
                    "ZEPETO 몸을 불러왔습니다. 쓸 수 있는 뼈 %d개 (%s). "
                    "뼈를 클릭하고 R을 누르세요" % (usable, tail))
        return {"FINISHED"}


class ZEPETO_OT_locate_paths(bpy.types.Operator):
    """
    Re-point both path fields at this machine's Unity project.

    Needed because the export button is disabled while the save folder is missing, so the export operator's own
    repair never gets a chance to run - without this the only fix left was the file browser or the Python
    console.
    """
    bl_idname = "zepeto.locate_paths"
    bl_label = "Unity 프로젝트 경로 자동 찾기"
    bl_description = "이 컴퓨터의 Unity 프로젝트를 찾아 저장 폴더와 리그 FBX 경로를 다시 채웁니다"
    bl_options = {"REGISTER", "UNDO"}

    def execute(self, context):
        scene = context.scene
        # force=True: the whole point of pressing this is to replace what is in the fields now.
        fix = refresh_paths(scene, force=True)

        # Report what was WRITTEN, never "a project was found". Reporting the folder meant the button announced
        # success while both fields were still empty - so the user read "찾았습니다", the export button stayed
        # disabled, and nothing in the UI said why.
        done = []
        if fix.export_dir:
            done.append("저장 폴더 → %s" % fix.export_dir)
        if fix.rig_fbx:
            done.append("리그 FBX → %s" % fix.rig_fbx)
        if done:
            self.report({"INFO"}, "경로를 다시 채웠습니다. " + " / ".join(done))
            return {"FINISHED"}

        # Nothing was written. Say exactly what is still missing and what the user has to do about it.
        missing = []
        if export_dir_problem(scene):
            missing.append("저장 폴더")
        rig_fbx = bpy.path.abspath(scene.zepeto_rig_fbx)
        if not rig_fbx or not os.path.isfile(rig_fbx):
            missing.append("리그 FBX")
        if not missing:
            # A real no-op: force rewrote nothing because both fields already point at things that exist.
            self.report({"INFO"}, "두 경로가 이미 올바릅니다. 바꾸지 않았습니다")
            return {"FINISHED"}

        if fix.ambiguous:
            # Two or more downloads side by side. Guessing here is how an export lands in a project Unity is
            # not watching, so the user picks.
            self.report({"ERROR"},
                        "Unity 프로젝트로 보이는 폴더가 %d개라서 고르지 못했습니다 (%s). "
                        "패널의 '저장 폴더'에서 쓰려는 프로젝트의 Assets/CustomMotions 폴더를 직접 고르세요 "
                        "(또는 환경 변수 %s에 그 프로젝트 폴더를 넣으세요). 아직 비어 있는 것: %s"
                        % (len(fix.ambiguous), " / ".join(fix.ambiguous),
                           UNITY_PROJECT_ENV, ", ".join(missing)))
        elif fix.project:
            self.report({"ERROR"},
                        "Unity 프로젝트는 찾았지만(%s) 그 안에 필요한 폴더가 없어서 %s 경로를 채우지 못했습니다. "
                        "Unity 헬퍼에서 'ZEPETO 리그 내보내기'를 먼저 누르거나, 패널에서 직접 고르세요"
                        % (fix.project, ", ".join(missing)))
        else:
            self.report({"ERROR"},
                        "Unity 프로젝트를 찾지 못해 %s 경로가 그대로 비어 있습니다. 패널에서 직접 지정하세요 "
                        "(또는 환경 변수 %s에 프로젝트 폴더를 넣으세요)" % (", ".join(missing), UNITY_PROJECT_ENV))
        return {"CANCELLED"}


class ZEPETO_OT_clear_pose(bpy.types.Operator):
    bl_idname = "zepeto.clear_pose"
    bl_label = "포즈 초기화"
    # Wording follows what execute() actually does: every rotation, but translation/scale only where the panel
    # flagged a change - the body's own shipped offsets are left alone.
    bl_description = "모든 뼈의 회전을 되돌리고, 이동·크기가 바뀐 뼈만 원래대로 돌립니다 (키프레임은 지우지 않습니다)"
    bl_options = {"REGISTER", "UNDO"}

    def execute(self, context):
        rig = get_rig(context)
        if not rig:
            self.report({"ERROR"}, "리그가 없습니다")
            return {"CANCELLED"}
        ensure_euler(rig)

        # Rotation alone was not enough: a bone dragged with G or resized with S stayed where it was, the
        # panel kept listing it under "이동/크기가 바뀐 뼈", and no button in the add-on could clear it.
        #
        # Rotation is cleared on EVERY bone - that is what "포즈 초기화" means. Translation and scale are
        # touched only on the bones the panel actually flagged (`cleared`: currently off-rest, minus the
        # zepeto_baseline_odd import snapshot, because the ZEPETO body ships a few non-unit pose scales).
        #
        # Resetting everything outside the snapshot instead flattened those shipped offsets and scales whenever
        # the snapshot was not there to protect them - a scene saved before this feature existed, a rig that
        # never came through step 1 - and a deviation too small for odd_bones' thresholds was destroyed even
        # with a snapshot present. That damage is silent until the export looks wrong in Unity.
        #
        # Sharing the one snapshot with the panel is what keeps this button's count and the warning's list
        # talking about the same set of bones.
        baseline = set(filter(None, (context.scene.zepeto_baseline_odd or "").split(",")))
        cleared = odd_bones(rig) - baseline
        for pb in rig.pose.bones:
            pb.rotation_euler = (0, 0, 0)
            if pb.name not in cleared:
                continue
            pb.location = (0.0, 0.0, 0.0)
            pb.scale = (1.0, 1.0, 1.0)
        self.report({"INFO"}, "포즈를 되돌렸습니다 (회전 전체 + 이동·크기가 바뀐 뼈 %d개)" % len(cleared))
        return {"FINISHED"}


class ZEPETO_OT_key_pose(bpy.types.Operator):
    bl_idname = "zepeto.key_pose"
    # Numbered to match the panel section this button lives in ("3단계 · 이 순간 기록"). The label is what F3
    # search and the Info log show, so an old number there sends the user to the wrong box.
    bl_label = "3. 현재 포즈 저장"
    bl_description = "지금 프레임에 현재 포즈를 키프레임으로 기록합니다"
    bl_options = {"REGISTER", "UNDO"}

    def execute(self, context):
        rig = get_rig(context)
        if not rig:
            self.report({"ERROR"}, "리그가 없습니다")
            return {"CANCELLED"}
        blocked = ensure_euler(rig)
        if blocked:
            self.report({"WARNING"},
                        "쿼터니언 애니메이션이 있는 뼈는 건너뜁니다(%d개): %s"
                        % (len(blocked), ", ".join(blocked[:3])))

        frame = context.scene.frame_current
        # Only the bones Unity's Humanoid avatar maps. Keying the other 49 would write curves that are
        # discarded at import anyway, and would make the "dead bone has keys" warning fire on every motion.
        kept = 0
        for pb in rig.pose.bones:
            if not is_mapped_bone(pb.name):
                continue
            pb.keyframe_insert(data_path="rotation_euler", frame=frame)
            kept += 1
        self.report({"INFO"}, "%d 프레임에 포즈를 저장했습니다 (뼈 %d개)" % (frame, kept))
        return {"FINISHED"}


class ZEPETO_OT_delete_key(bpy.types.Operator):
    bl_idname = "zepeto.delete_key"
    bl_label = "이 프레임 키 삭제"
    bl_description = "지금 프레임의 키프레임을 지웁니다"
    bl_options = {"REGISTER", "UNDO"}

    def execute(self, context):
        rig = get_rig(context)
        if not rig:
            self.report({"ERROR"}, "리그가 없습니다")
            return {"CANCELLED"}
        frame = context.scene.frame_current

        # Both rotation representations, and count what actually went away.
        #
        # rotation_euler only was a silent lie: ensure_euler deliberately leaves a bone that already carries
        # quaternion F-curves in QUATERNION mode (flipping it would orphan real animation), so such bones exist
        # by design - their keys stayed on the timeline while this operator still reported "지웠습니다".
        removed = 0
        for pb in rig.pose.bones:
            hit = False
            for data_path in ("rotation_euler", "rotation_quaternion"):
                try:
                    if pb.keyframe_delete(data_path=data_path, frame=frame):
                        hit = True
                except RuntimeError:
                    pass
            if hit:
                removed += 1
        if not removed:
            self.report({"WARNING"}, "%d 프레임에는 지울 키가 없습니다" % frame)
            return {"CANCELLED"}
        self.report({"INFO"}, "%d 프레임 키를 지웠습니다 (뼈 %d개)" % (frame, removed))
        return {"FINISHED"}


class ZEPETO_OT_make_loop(bpy.types.Operator):
    bl_idname = "zepeto.make_loop"
    # Matches the panel section "4단계 · 부드럽게 반복" (see the note on key_pose's label).
    bl_label = "4. 반복되게 만들기"
    bl_description = "첫 프레임 포즈를 마지막 프레임에 복사해 자연스럽게 반복되게 합니다"
    bl_options = {"REGISTER", "UNDO"}

    def execute(self, context):
        rig = get_rig(context)
        if not rig:
            self.report({"ERROR"}, "리그가 없습니다")
            return {"CANCELLED"}
        scene = context.scene
        start, end = scene.frame_start, scene.frame_end

        # Before frame_set/snapshot: reading rotation_euler off a QUATERNION bone yields zeros, and the loop
        # would then copy a rest pose onto the last frame.
        ensure_euler(rig)

        scene.frame_set(start)
        snapshot = {pb.name: tuple(pb.rotation_euler) for pb in rig.pose.bones}

        scene.frame_set(end)
        # Same mapped-only rule as key_pose, so looping cannot reintroduce curves on discarded bones.
        for pb in rig.pose.bones:
            if not is_mapped_bone(pb.name):
                continue
            pb.rotation_euler = snapshot[pb.name]
            pb.keyframe_insert(data_path="rotation_euler", frame=end)

        self.report({"INFO"}, "%d 프레임을 시작 포즈와 같게 맞췄습니다" % end)
        return {"FINISHED"}


class ZEPETO_OT_export(bpy.types.Operator):
    bl_idname = "zepeto.export"
    # Matches the panel section "5단계 · Unity로 보내기" (see the note on key_pose's label).
    bl_label = "5. Unity로 내보내기"
    bl_description = "ZEPETO용 설정으로 FBX를 Unity 프로젝트에 바로 저장합니다"

    def execute(self, context):
        rig = get_rig(context)
        if not rig:
            self.report({"ERROR"}, "리그가 없습니다")
            return {"CANCELLED"}

        scene = context.scene
        # Repair an empty or inherited-from-another-machine folder before judging it. The panel gates the
        # button on the same check (export_dir_problem), so normally this has nothing left to do.
        refresh_paths(scene)
        folder_problem = export_dir_problem(scene)
        if folder_problem:
            self.report({"ERROR"}, folder_problem)
            return {"CANCELLED"}
        folder = bpy.path.abspath(scene.zepeto_export_dir)

        # Validated rather than silently repaired: "   " used to pass the `or` fallback, strip to "", and
        # write a file literally called ".fbx"; forbidden Windows characters escaped as a raw traceback.
        name_problem = motion_name_problem(scene.zepeto_motion_name)
        if name_problem:
            self.report({"ERROR"}, name_problem)
            return {"CANCELLED"}

        name = scene.zepeto_motion_name.strip()
        path = os.path.join(folder, name + ".fbx")

        # Write to a scratch name and swap it in atomically.
        #
        # Unity watches this folder and reimports the moment the file's timestamp moves. Writing the fbx in
        # place means Unity can start reading a half-written file and bake a corrupt clip - and on Windows,
        # Unity holding the previous fbx open makes the write itself fail with PermissionError. os.replace is
        # atomic on the same volume, so Unity only ever sees a complete file.
        temp_path = path + ".part"
        # A stale .part from an export that died mid-write would otherwise block this one.
        if os.path.exists(temp_path):
            try:
                os.remove(temp_path)
            except OSError:
                pass

        # [AUDIT] Export the whole node graph, not just the armature.
        #
        # MESH: ZEPETO's bone names (hips, upperArm_R, and the right leg is even misspelled upperReg_R) are not
        # names Unity's humanoid auto-mapper knows. Without the skinned mesh Unity builds a generic avatar
        # (isHuman=false) and then generates zero humanoid AnimationClips - the import looks empty.
        #
        # EMPTY is deliberately excluded. The rig fbx carries a wrapper empty (ZepetoBaseModel > hips > body),
        # but Unity always names an imported model's root after the FILE. Exporting the wrapper as well just
        # nests it under Unity's own root and adds a redundant level (107 transforms instead of 106).
        object_types = {"ARMATURE", "MESH"}

        # Export ONLY the rig and its own meshes.
        #
        # use_selection=False shipped whatever happened to be in the .blend - Blender's default cube in a
        # fresh install, or a second armature, which makes Unity's humanoid mapper fail and surfaces to the
        # user as unrelated advice about import settings.
        #
        # hide_select must be cleared first: import_rig sets it on the body so clicks land on bones, and
        # Blender's select_set silently no-ops on an unselectable object. Missing that would leave the skinned
        # mesh out and produce the isHuman=false export the AUDIT note above warns about.
        wanted = [rig] + [o for o in rig.children_recursive if o.type in object_types]
        prev_selected = [o for o in context.view_layer.objects if o.select_get()]
        prev_active = context.view_layer.objects.active
        prev_hide = {o: o.hide_select for o in wanted}

        # Any exporter failure has to come out as a Korean report like every other failure here. Unwrapped, an
        # error inside export_scene.fbx (a locked path, an unwritable folder, an encoding the exporter chokes
        # on) escaped as a raw Python traceback in the system console - which a beginner never sees, so the
        # button simply looked like it did nothing.
        export_error = None
        try:
            for o in wanted:
                o.hide_select = False
            for o in context.view_layer.objects:
                o.select_set(False)
            for o in wanted:
                o.select_set(True)
            context.view_layer.objects.active = rig

            bpy.ops.export_scene.fbx(
                filepath=temp_path,
                use_selection=True,
                object_types=object_types,
                add_leaf_bones=False,          # leaf bones break Unity humanoid mapping
                bake_anim=True,
                bake_anim_use_all_bones=True,
                bake_anim_use_nla_strips=False,
                bake_anim_use_all_actions=False,
                bake_anim_force_startend_keying=True,
                bake_anim_step=1.0,
                bake_anim_simplify_factor=0.0,
                apply_scale_options="FBX_SCALE_ALL",
                axis_forward="-Z",
                axis_up="Y",
                global_scale=1.0,
            )
        except Exception as exc:
            export_error = exc
        finally:
            for o, hidden in prev_hide.items():
                o.hide_select = hidden
            for o in context.view_layer.objects:
                o.select_set(False)
            for o in prev_selected:
                try:
                    o.select_set(True)
                except ReferenceError:
                    pass
            context.view_layer.objects.active = prev_active

        if export_error is not None or not os.path.exists(temp_path):
            # Same cleanup contract as the os.replace failure below: never leave a half-written .part inside
            # Assets/, where a manual AssetDatabase.Refresh would import it and mint an orphan .meta.
            try:
                os.remove(temp_path)
            except OSError:
                pass
            if export_error is None:
                self.report({"ERROR"}, "FBX를 만들지 못했습니다")
            else:
                self.report({"ERROR"}, "FBX를 만들지 못했습니다: %s" % export_error)
            return {"CANCELLED"}

        # Unity can hold the destination open while reimporting it, which surfaces as PermissionError here.
        #
        # The old flat 5 x 0.2s gave up after ~0.8s, but the thing being waited on is
        # AssetDatabase.ImportAsset(ForceUpdate) on a ~1.6 MB fbx, which regularly takes longer than that -
        # so the user got "잠시 뒤 다시 누르세요" for something that would have healed itself. Backoff, and
        # no sleep after the final attempt.
        delays = (0.1, 0.2, 0.4, 0.8, 1.6, 2.0)
        last_error = None
        for attempt, delay in enumerate(delays):
            try:
                os.replace(temp_path, path)
                last_error = None
                break
            except PermissionError as exc:
                last_error = exc
                if attempt < len(delays) - 1:
                    time.sleep(delay)

        if last_error is not None:
            # Never leave the scratch file inside Assets/: Unity's watcher skips ".part", but a manual
            # AssetDatabase.Refresh imports it as a DefaultAsset and mints an orphan .meta.
            try:
                os.remove(temp_path)
            except OSError:
                pass
            self.report({"ERROR"},
                        "Unity가 파일을 잡고 있어서 저장하지 못했습니다. 잠시 뒤 다시 누르세요 (%s)" % path)
            return {"CANCELLED"}

        self.report({"INFO"}, "내보냈습니다: %s" % path)
        return {"FINISHED"}


# ---------------------------------------------------------------- panel
class ZEPETO_PT_panel(bpy.types.Panel):
    bl_label = "ZEPETO 모션"
    bl_idname = "ZEPETO_PT_panel"
    bl_space_type = "VIEW_3D"
    bl_region_type = "UI"
    bl_category = "ZEPETO"

    def draw(self, context):
        draw_zepeto_panel(self.layout, context)


class ZEPETO_PT_panel_item(bpy.types.Panel):
    """
    Mirror of the same panel in Blender's default "Item" tab.

    The sidebar opens on Item, and Python cannot set the active tab (region.active_panel_category is
    read-only), so a first-timer would see an empty Transform panel and have to discover the ZEPETO tab
    on the vertical strip. Showing the same controls in Item removes that step entirely.
    """
    bl_label = "ZEPETO 모션"
    bl_idname = "ZEPETO_PT_panel_item"
    bl_space_type = "VIEW_3D"
    bl_region_type = "UI"
    bl_category = "Item"

    def draw(self, context):
        draw_zepeto_panel(self.layout, context)


def draw_zepeto_panel(layout, context):
        scene = context.scene
        rig = get_rig(context)

        if not rig:
            box = layout.box()
            box.label(text="1단계 · 몸 불러오기", icon="ARMATURE_DATA")
            box.operator("zepeto.import_rig", text="ZEPETO 몸 불러오기", icon="IMPORT")
            box.prop(scene, "zepeto_rig_fbx", text="")
            # The field starts empty on purpose (see refresh_paths); say so, or an empty box reads as broken.
            rig_fbx = bpy.path.abspath(scene.zepeto_rig_fbx)
            if not rig_fbx or not os.path.isfile(rig_fbx):
                box.label(text="비워 두면 불러올 때 자동으로 찾습니다", icon="INFO")
                box.operator("zepeto.locate_paths", text="경로 자동 찾기", icon="FILE_REFRESH")
            return

        usable = sum(1 for b in rig.data.bones if is_mapped_bone(b.name))
        box = layout.box()
        box.label(text="쓸 수 있는 뼈 %d개 / 전체 %d개" % (usable, len(rig.data.bones)), icon="CHECKMARK")
        # 54 is the ceiling: "hips" is the armature object, never a bone. Anything well below that means the
        # rig was re-exported with different bone names and MAPPED_BONES is stale - which would otherwise show
        # up as a hidden skeleton with a reassuring checkmark.
        if usable < len(MAPPED_BONES) - 1:
            box.label(text="이 리그는 ZEPETO 기본 몸과 뼈 이름이 다릅니다. 3번에서 다시 내보내세요",
                      icon="ERROR")

        box = layout.box()
        box.label(text="2단계 · 포즈 만들기", icon="POSE_HLT")
        box.label(text="뼈를 클릭 → R → Z", icon="INFO")
        box.label(text="→ 마우스 이동 → 좌클릭", icon="BLANK1")
        box.operator("zepeto.clear_pose", text="포즈 전부 되돌리기")
        box.prop(scene, "zepeto_show_all_bones")
        if scene.zepeto_show_all_bones:
            box.label(text="숨겨둔 뼈는 돌려도 Unity에서 사라집니다", icon="ERROR")

        box = layout.box()
        box.label(text="3단계 · 이 순간 기록", icon="KEYFRAME_HLT")
        row = box.row()
        row.scale_y = 1.2
        row.prop(scene, "frame_current", text="프레임")
        col = box.column()
        col.scale_y = 1.4
        col.operator("zepeto.key_pose", text="현재 포즈 저장", icon="KEY_HLT")
        box.operator("zepeto.delete_key", text="이 프레임 지우기", icon="KEY_DEHLT")

        times = keyframe_times(rig)
        box.label(text="저장된 프레임: %s" % (", ".join(str(t) for t in times) if times else "아직 없음"))

        box = layout.box()
        box.label(text="4단계 · 부드럽게 반복", icon="LOOP_BACK")
        box.operator("zepeto.make_loop", text="처음과 끝 맞추기")

        # Problems that would make the avatar stand still or drift out of the booth framing.
        problems = []
        if scene.render.fps != FPS:
            problems.append("fps가 %d입니다. 24로 바꾸세요" % scene.render.fps)
        if len(times) < 2:
            problems.append("키프레임이 %d개입니다. 2개 이상 필요합니다" % len(times))
        # Humanoid retargeting only carries rotation, so a moved or resized bone is wasted work, and a moved
        # root walks the avatar out of the booth framing. Only flag what the user changed since import.
        baseline = set(filter(None, (scene.zepeto_baseline_odd or "").split(",")))
        changed = sorted(odd_bones(rig) - baseline)
        if changed:
            problems.append("이동/크기가 바뀐 뼈: %s (회전만 쓰세요)" % ", ".join(changed[:3]))
        # Only reachable if the user unhid the dead bones and keyed one. Unity would drop these without a
        # word, so catching it here is the last chance to say anything at all.
        dead = keyed_dead_bones(rig)
        if dead:
            problems.append("Unity가 무시하는 뼈에 키가 있습니다: %s (지우거나 매핑된 뼈로 다시 만드세요)"
                            % ", ".join(dead[:3]))

        box = layout.box()
        box.label(text="5단계 · Unity로 보내기", icon="EXPORT")
        box.prop(scene, "zepeto_motion_name", text="이름")
        # The save folder used to be invisible in the UI: a .blend carrying another machine's path failed at the
        # export button with no way to fix it short of the Python console. Same problems list as everything else
        # in this box, so the button keeps its single gate.
        box.prop(scene, "zepeto_export_dir", text="저장 폴더")

        name_problem = motion_name_problem(scene.zepeto_motion_name)
        if name_problem:
            problems.append(name_problem)

        folder_problem = export_dir_problem(scene)
        if folder_problem:
            problems.append(folder_problem)
            box.label(text="이 폴더를 찾을 수 없습니다. 아래 버튼을 누르거나 폴더를 직접 고르세요", icon="ERROR")
            box.operator("zepeto.locate_paths", text="경로 자동 찾기", icon="FILE_REFRESH")

        if problems:
            box.label(text="아직 안 됩니다", icon="ERROR")
            for p in problems:
                box.label(text="· " + p)
        else:
            box.label(text="보낼 준비 완료", icon="CHECKMARK")

        col = box.column()
        col.scale_y = 1.4
        col.enabled = not problems
        col.operator("zepeto.export", text="Unity로 보내기", icon="EXPORT")


CLASSES = (
    ZEPETO_OT_import_rig,
    ZEPETO_OT_locate_paths,
    ZEPETO_OT_clear_pose,
    ZEPETO_OT_key_pose,
    ZEPETO_OT_delete_key,
    ZEPETO_OT_make_loop,
    ZEPETO_OT_export,
    ZEPETO_PT_panel,
    ZEPETO_PT_panel_item,
)


def register():
    for c in CLASSES:
        bpy.utils.register_class(c)
    bpy.types.Scene.zepeto_motion_name = StringProperty(
        name="이름", default="MyMotion",
        description="내보낼 FBX 파일 이름")
    # Both path properties start EMPTY and are filled by refresh_paths on first use. `default=` is evaluated
    # once here, at registration, so anything baked in would freeze one machine's folder into every new scene.
    bpy.types.Scene.zepeto_export_dir = StringProperty(
        name="저장 폴더", subtype="DIR_PATH", default="",
        description="Unity 프로젝트의 Assets/CustomMotions 폴더. 비워 두면 자동으로 찾습니다")
    bpy.types.Scene.zepeto_baseline_odd = StringProperty(
        name="baseline", default="",
        description="Bones already off-rest when the rig was imported")
    bpy.types.Scene.zepeto_rig_fbx = StringProperty(
        name="ZEPETO FBX", subtype="FILE_PATH", default="",
        description="Unity 헬퍼의 'ZEPETO 리그 내보내기'가 만든 FBX. 비워 두면 자동으로 찾습니다")
    bpy.types.Scene.zepeto_show_all_bones = BoolProperty(
        name="Unity가 무시하는 뼈도 보기", default=False,
        description="켜면 Twist·_scale·얼굴 뼈까지 전부 보입니다. 그 뼈들을 돌려도 Unity에서는 사라집니다",
        update=_on_show_all_bones_changed)


def unregister():
    for c in reversed(CLASSES):
        bpy.utils.unregister_class(c)
    del bpy.types.Scene.zepeto_motion_name
    del bpy.types.Scene.zepeto_export_dir
    del bpy.types.Scene.zepeto_baseline_odd
    del bpy.types.Scene.zepeto_rig_fbx
    del bpy.types.Scene.zepeto_show_all_bones


if __name__ == "__main__":
    register()
