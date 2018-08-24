using UnityEngine;

// Represents a single change to the state of the board
public class BoardState
{
    public ConnectFour board = new ConnectFour();

    // The column/row that was changed in this move
    // as well as the player whose move it is
    public BoardMove move;

    // The resulting heuristic score for this move. This score is the best interest for this playerId. The score will be negative for the non-NPC player
    public int score = 0;

    public TreeNode<BoardState> bestScoreNode;

    public struct BoardMove
    {
        // The player whose move this is
        public byte playerId;

        // The column and row of the resulting drop
        public int column, row;

        public Vector2Int coord
        {
            get { return new Vector2Int(column, row); }
        }

        public BoardMove(byte playerId, int column, int row)
        {
            this.playerId = playerId;
            this.column = column;
            this.row = row;
        }
    }

    public BoardState() { }

    public BoardState(byte playerId)
    {
        this.move.playerId = playerId;
    }

    public void CloneFrom(BoardState source)
    {
        CloneFrom(source.board);
    }

    public void CloneFrom(ConnectFour source)
    {
        CloneFrom(source.GetBoard());
    }

    public void CloneFrom(byte[,] source)
    {
        var dst = ConnectFour.EmptyBoard;
        System.Array.Copy(source, dst, source.Length);

        this.board.SetBoard(dst);
    }

    // Does the move for this board in a specific column.
    // If this returns true, the move resulted in a win and we should stop constructing the board deeper than this point
    public bool DoMove(int column)
    {
        if (!ConnectFour.IsPlayerValid(move.playerId))
        {
            Debug.LogError("BoardState : Set valid playerId first!");
            return false;
        }

        move.column = column;
        board.Drop(move.playerId, column, out move.row);
        UpdateScore();

        // Return true if the column is now occupied, or the move won, or if it was a stalemate
        return board.IsSpaceOccupied(column, 0) || board.CheckForWinIncremental(move.playerId, move.coord) || board.CheckForStalemate();
    }

    // Get a heuristic score for the move
    public void UpdateScore()
    {
        score = board.GetHeuristicScore_Method1(move.playerId, move.coord);
        //score = board.GetHeuristicScore_Method2(2, 1, move.playerId == 2);
    }

    public override string ToString()
    {
        return string.Format("BoardState (coord = x{0}y{1}, playerId = {2}, score = {3}\n\n{4})", move.column, move.row, move.playerId, score, board.ToString());
    }
}