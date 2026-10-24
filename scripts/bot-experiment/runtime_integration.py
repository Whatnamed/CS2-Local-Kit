"""Use the existing exact transaction with a separate marker over the playable baseline."""
import sys
sys.dont_write_bytecode = True
import bot_experiment as tx

tx.MARKER = tx.INTEGRATION_MARKER
if __name__ == '__main__':
    tx.main()
