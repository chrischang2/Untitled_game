extends Node

signal currency_changed(new_value: int)

var currency := 0

func add_currency(amount: int) -> void:
	currency += amount
	currency_changed.emit(currency)

func spend_currency(amount: int) -> bool:
	if currency < amount:
		return false
	currency -= amount
	currency_changed.emit(currency)
	return true
