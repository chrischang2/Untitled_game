extends RigidBody3D

const CHUNK_SIZE := 0.25

func _ready() -> void:
	add_to_group("currency_chunk")
	mass = 0.3

	var box_mesh := BoxMesh.new()
	box_mesh.size = Vector3(CHUNK_SIZE, CHUNK_SIZE, CHUNK_SIZE)
	var material := StandardMaterial3D.new()
	material.albedo_color = Color(0.85, 0.82, 0.7)
	box_mesh.material = material

	var mesh_instance := MeshInstance3D.new()
	mesh_instance.mesh = box_mesh
	add_child(mesh_instance)

	var shape := CollisionShape3D.new()
	var box_shape := BoxShape3D.new()
	box_shape.size = Vector3(CHUNK_SIZE, CHUNK_SIZE, CHUNK_SIZE)
	shape.shape = box_shape
	add_child(shape)


func collect() -> void:
	GameState.add_currency(1)
	queue_free()
