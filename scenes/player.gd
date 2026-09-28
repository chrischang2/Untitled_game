extends CharacterBody3D

const BASE_SPEED := 4.0
const GRAVITY := 12.0
const TURN_SPEED := 10.0
const PUNCH_RANGE := 1.8
const PUNCH_COOLDOWN := 0.4

@onready var _pivot: Node3D = $Pivot
@onready var _model: Node3D = $Pivot/Model
@onready var _anim_player: AnimationPlayer = _find_animation_player(self)
@onready var _pickup_area: Area3D = $PickupArea
var _current_anim := ""
var _punch_cooldown_left := 0.0

var speed := BASE_SPEED
var punch_damage := 1

func _ready() -> void:
	_apply_character_colors()
	_pickup_area.body_entered.connect(_on_pickup_area_body_entered)


func _physics_process(delta: float) -> void:
	if not is_on_floor():
		velocity.y -= GRAVITY * delta

	if _punch_cooldown_left > 0.0:
		_punch_cooldown_left -= delta
	if Input.is_action_just_pressed("punch") and _punch_cooldown_left <= 0.0:
		_punch()
		_punch_cooldown_left = PUNCH_COOLDOWN

	var input_dir := Input.get_vector("ui_left", "ui_right", "ui_up", "ui_down")
	var raw_dir := Vector3(input_dir.x, 0.0, input_dir.y)

	if raw_dir.length() > 0.0:
		# Input is relative to the camera's current facing, not fixed world axes.
		var direction := (_pivot.global_transform.basis * raw_dir)
		direction.y = 0.0
		direction = direction.normalized()
		velocity.x = direction.x * speed
		velocity.z = direction.z * speed
		# Rotate the pivot (model + camera) smoothly; the body itself never rotates.
		_pivot.rotation.y = lerp_angle(_pivot.rotation.y, atan2(-direction.x, -direction.z), TURN_SPEED * delta)
		if _punch_cooldown_left <= 0.0:
			_play_animation(["Walk", "walk", "Walking"])
	else:
		velocity.x = move_toward(velocity.x, 0.0, speed)
		velocity.z = move_toward(velocity.z, 0.0, speed)
		if _punch_cooldown_left <= 0.0:
			_play_animation(["Idle", "idle"])

	move_and_slide()


func _punch() -> void:
	var space_state := get_world_3d().direct_space_state
	var forward := -_pivot.global_transform.basis.z
	var origin := global_position + Vector3(0, 1.0, 0)
	var query := PhysicsRayQueryParameters3D.create(origin, origin + forward * PUNCH_RANGE)
	query.exclude = [get_rid()]
	var result := space_state.intersect_ray(query)
	if result and result.collider is Node and result.collider.is_in_group("destroyable_chunk"):
		result.collider.take_damage(punch_damage)
	_play_animation(["attack-melee-right", "attack-melee-left"])
	_play_punch_lunge()


func _play_punch_lunge() -> void:
	var base_pos := Vector3.ZERO
	var tween := create_tween()
	tween.tween_property(_model, "position", base_pos + Vector3(0, 0, -0.35), 0.08).set_trans(Tween.TRANS_QUAD).set_ease(Tween.EASE_OUT)
	tween.tween_property(_model, "position", base_pos, 0.2).set_trans(Tween.TRANS_QUAD).set_ease(Tween.EASE_IN)


func _on_pickup_area_body_entered(body: Node) -> void:
	if body.is_in_group("currency_chunk") and body.has_method("collect"):
		body.collect()


func upgrade_speed(amount: float) -> void:
	speed += amount


func upgrade_punch_damage(amount: int) -> void:
	punch_damage += amount


func upgrade_pickup_radius(amount: float) -> void:
	var shape: CollisionShape3D = _pickup_area.get_child(0)
	var sphere: SphereShape3D = shape.shape
	sphere.radius += amount


func _play_animation(candidates: Array) -> void:
	if _anim_player == null:
		return
	for candidate in candidates:
		if _anim_player.has_animation(candidate):
			if _current_anim != candidate:
				_anim_player.play(candidate)
				_current_anim = candidate
			return


func _find_animation_player(node: Node) -> AnimationPlayer:
	if node is AnimationPlayer:
		return node
	for child in node.get_children():
		var result := _find_animation_player(child)
		if result:
			return result
	return null


# The Blocky Characters mesh ships with plain white materials meant to be tinted per-part.
func _apply_character_colors() -> void:
	var colors := {
		"head": Color(0.87, 0.68, 0.53),
		"torso": Color(0.2, 0.4, 0.8),
		"arm-left": Color(0.87, 0.68, 0.53),
		"arm-right": Color(0.87, 0.68, 0.53),
		"leg-left": Color(0.25, 0.25, 0.3),
		"leg-right": Color(0.25, 0.25, 0.3),
	}
	for mesh_instance in _find_all_mesh_instances(_model):
		if colors.has(mesh_instance.name):
			var mat := StandardMaterial3D.new()
			mat.albedo_color = colors[mesh_instance.name]
			mesh_instance.set_surface_override_material(0, mat)


func _find_all_mesh_instances(node: Node) -> Array:
	var result: Array = []
	if node is MeshInstance3D:
		result.append(node)
	for child in node.get_children():
		result.append_array(_find_all_mesh_instances(child))
	return result
