extends StaticBody3D

signal destroyed

@export var size: Vector3 = Vector3(2.0, 1.0, 0.3)
@export var max_health := 3

var _health: int
var _material: StandardMaterial3D

func _ready() -> void:
	_health = max_health
	add_to_group("destroyable_chunk")

	var box_mesh := BoxMesh.new()
	box_mesh.size = size
	_material = StandardMaterial3D.new()
	_material.albedo_color = Color(0.96, 0.96, 0.93)
	box_mesh.material = _material

	var mesh_instance := MeshInstance3D.new()
	mesh_instance.mesh = box_mesh
	add_child(mesh_instance)

	var shape := CollisionShape3D.new()
	var box_shape := BoxShape3D.new()
	# Slightly larger than the visual mesh so the cosmetic seam gaps don't create raycast gaps.
	box_shape.size = size * 1.1
	shape.shape = box_shape
	add_child(shape)


func take_damage(amount: int = 1) -> void:
	_health -= amount
	_material.albedo_color = _material.albedo_color.darkened(0.2)
	if _health <= 0:
		_break()


func _break() -> void:
	_spawn_currency(randi_range(1, 3))
	destroyed.emit()
	queue_free()


func _spawn_currency(count: int) -> void:
	var parent := get_parent()
	for i in range(count):
		var piece := RigidBody3D.new()
		piece.set_script(load("res://scripts/currency_chunk.gd"))
		parent.add_child(piece)
		piece.global_position = global_position + Vector3(randf_range(-0.3, 0.3), randf_range(-0.2, 0.2), randf_range(-0.1, 0.3))
		piece.apply_impulse(Vector3(randf_range(-1.0, 1.0), randf_range(1.5, 3.0), randf_range(0.8, 2.0)))
