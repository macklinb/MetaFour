// ConnectFour.cs
// All the game logic for the game of Connect-Four. This is purposely kept separate to any networking logic

using System.Linq;
using System.Collections.Generic;
using UnityEngine;

public class ConnectFour
{
    // Public constants

    // The amount of discs in a row we need to win
    public const int SEQ_COUNT = 4;

    // Dimensions of the board
    public const int BOARD_WIDTH = 7;
    public const int BOARD_HEIGHT = 6;

    public const byte PLAYER_ONE = 1;
    public const byte PLAYER_TWO = 2;

    public const string SEQ_TYPE_HORIZONTAL = "horizontal";
    public const string SEQ_TYPE_VERTICAL = "vertical";
    public const string SEQ_TYPE_DIAGONAL_LEFT = "diagonal-left";
    public const string SEQ_TYPE_DIAGONAL_RIGHT = "diagonal-right";

    // Private variables

    // Multidimentional array containing the positions of both players in the form of bytes.
    // 0 is unoccupied, 1 is player 1, 2 is player 2. X is from left to right, Y is from top to bottom
    // x0y0 is top left, x{BOARD_WIDTH - 1}y0 is top right, etc.
    byte[,] board = new byte[BOARD_WIDTH, BOARD_HEIGHT];

    // Returns a new empty byte[,]
    public static byte[,] EmptyBoard
    {
        get { return new byte[BOARD_WIDTH, BOARD_HEIGHT];  }
    }

    // Static variables

    // winningSequences is an array of arrays of 4 coordinates (1 for each token) where a winning sequence is possible. While we take more of a mathematical approach to checking for wins, the heuristic function takes more of a brute force approach using these pre-calculated arrays.
    public static List<Vector2Int[]> winningSequences = new List<Vector2Int[]>();

    static ConnectFour instance;
    public static ConnectFour Instance
    {
        get
        {
            if (instance == null)
                instance = new ConnectFour();
            
            return instance;
        }
    }

    // Static ctor
    static ConnectFour()
    {
        CalculatetWinningSequences();
    }

    // --- PUBLIC STATIC METHODS ---

    public static bool IsCoordInRange(Vector2Int coord)
    {
        return IsCoordInRange(coord.x, coord.y);
    }

    public static bool IsCoordInRange(int x, int y)
    {
        if (IsColumnInRange(x) && IsRowInRange(y))
            return true;

        return false;
    }

    public static bool IsColumnInRange(int column)
    {
        if (column < 0 || column >= BOARD_WIDTH)
            return false;

        return true;
    }

    public static bool IsRowInRange(int row)
    {
        if (row < 0 || row >= BOARD_HEIGHT)
            return false;

        return true;
    }

    public static bool IsPlayerValid(byte player)
    {
        // Make sure the player value is within range
        if (player != PLAYER_ONE && player != PLAYER_TWO)
        {
            Debug.LogError("ConnectFour : Player value is invalid (needs to be 1 or 2)");
            return false;
        }

        return true;
    }

    // Used when interchanging coordinates between players
    public static Vector2Int InvertCoord(Vector2Int coord)
    {
        return new Vector2Int(InvertColumn(coord.x), coord.y);
    }

    public static int InvertColumn(int x, bool isIndex = true)
    {
        if (!IsColumnInRange(x))
            return -1;

        return (BOARD_WIDTH - (isIndex ? 1 : 0) - x);
    }

    // --- PUBLIC METHODS ---

    // Returns true if the space at X/Y is non-occupied
    public bool IsSpaceFree(int x, int y)
    {
        // Return false if coordinate is out of range
        if (x < 0 || x >= BOARD_WIDTH || y < 0 || y >= BOARD_HEIGHT)
        {
            Debug.LogFormat("ConnectFour : IsSpaceFree(x: {0}, y: {1}) out of range", x, y);
            return false;
        }

        return board[x, y] == 0;
    }

    // Inverse of the above
    public bool IsSpaceOccupied(int x, int y)
    {
        return !IsSpaceFree(x, y);
    }

    // Drops a players token in a specific column. Returns true if successful, and the row that the token would have dropped to.
    public bool Drop(byte player, int column, out int row)
    {
        row = -1;

        // Don't continue if the column is out of range
        if (!IsColumnInRange(column))
            return false;

        // Don't continue if the player isn't 1 or 2
        if (!IsPlayerValid(player))
            return false;

        // Check if see if the column is occupied, by seeing if the first space is free
        if (IsSpaceOccupied(column, 0))
        {
            Debug.LogErrorFormat("ConnectFour : Drop({0}, {1}) - Column {0} is full!", column, player);
            return false;
        }

        // Finally, drop the disc if all of the above checks pass
        for (int y = 0; y < BOARD_HEIGHT; y++)
        {
            // Check if the next space downwards is occupied, or if there is a next space
            if ((y + 1 >= BOARD_HEIGHT) || IsSpaceOccupied(column, y + 1))
            {
                row = y;
                PutDisc(player, column, row);
                return true;
            }
        }

        // If we're here, something happened that shouldn't have
        Debug.LogErrorFormat("ConnectFour : Drop({0}, {1}) - An unknown error occurred!", column, player);
        return false;
    }

    // Sets the board to the byte[,] newBoard, by reference
    public void SetBoard(byte[,] newBoard)
    {
        board = newBoard;
    }

    // Returns the byte[,] board
    public byte[,] GetBoard()
    {
        return board;
    }

    // Clears the board by setting it to a new empty board
    public void WipeBoard()
    {
        board = ConnectFour.EmptyBoard;
    }

    // Logs the board array to the console
    public void PrintBoard()
    {
        Debug.Log("ConnectFour : Current board\n<Click to view>\n" + this.ToString());
    }

    // Returns a string representation of the board array
    public override string ToString()
    {
        /*
        // Type A
        var sb = new System.Text.StringBuilder("      ");

        // Print header
        for (int x = 0; x < BOARD_WIDTH; x++)
            sb.Append(x + "   ");

        sb.Append("\n");

        for (int y = 0; y < BOARD_HEIGHT; y++)
        {
            sb.Append(y + " |  ");

            for (int x = 0; x < BOARD_WIDTH; x++)
                sb.Append(board[x, y] + "   ");

            sb.Append("\n");
        }
        */

        // Type B
        var sb = new System.Text.StringBuilder();

        for (int y = 0; y < BOARD_HEIGHT; y++)
        {
            for (int x = 0; x < BOARD_WIDTH; x++)
                sb.Append(board[x, y] + ((x < BOARD_WIDTH - 1) ? " | " : ""));

            sb.Append("\n");
        }

        return sb.ToString();
    }

    // If there are no more spots free, it is a stalemate
    public bool CheckForStalemate()
    {
        for (int x = 0; x < BOARD_WIDTH; x++)
        {
            for (int y = 0; y < BOARD_HEIGHT; y++)
            {
                if (IsSpaceFree(x, y))
                    return false;
            }
        }

        return true;
    }

    // Returns true if the player with the playerId won
    public bool CheckForWin(byte playerId)
    {
        Vector2Int start, end; string seqType;
        return CheckForWin(playerId, out start, out end, out seqType);
    }

    // Returns true if the player with the playerId won using a specific cell. This only checks for sequences around the cell, including it
    public bool CheckForWinIncremental(byte playerId, Vector2Int cell)
    {
        // Return false immediately if the cell is unoccupied, or occupied by the other player
        if (board[cell.x, cell.y] != playerId)
            return false;

        // No out variable declaration in C# v4 :(
        Vector2Int start, end; string seqType;

        return CheckForWinIncremental(playerId, cell, out start, out end, out seqType);
    }

    // Checks for a win by the player across the entire board, returning true if the player won, false otherwise. The out values are the start and end coordinates of the winning row, as well as the type of win that was achieved
    public bool CheckForWin(byte player, out Vector2Int start, out Vector2Int end, out string seqType)
    {
        int startX, startY, endX, endY, row, column;
        seqType = "";

        if (CheckHorizontalWin(player, out startX, out endX, out row))
        {
            seqType = SEQ_TYPE_HORIZONTAL;
            start = new Vector2Int(startX, row);
            end = new Vector2Int(endX, row);
        }
        else if (CheckVerticalWin(player, out startY, out endY, out column))
        {
            seqType = SEQ_TYPE_VERTICAL;
            start = new Vector2Int(column, startY);
            end = new Vector2Int(column, endY);
        }
        else if (CheckDiagonalLeftWin(player, out startX, out endY))
        {
            seqType = SEQ_TYPE_DIAGONAL_LEFT;
            start = new Vector2Int(startX, endY - (ConnectFour.SEQ_COUNT - 1));
            end = new Vector2Int(startX + (ConnectFour.SEQ_COUNT - 1), endY);
        }
        else if (CheckDiagonalRightWin(player, out startX, out endY))
        {
            seqType = SEQ_TYPE_DIAGONAL_RIGHT;
            start = new Vector2Int(startX, endY - (ConnectFour.SEQ_COUNT - 1));
            end = new Vector2Int(startX - (ConnectFour.SEQ_COUNT - 1), endY);
        }
        else
        {
            Debug.Log("ConnectFour : CheckForWin - No wins found...");
            start = end = new Vector2Int(-1, -1);
            return false;
        }

        Debug.LogFormat("ConnectFour : CheckForWin - Player {0} wins! Used a {1} sequence, from {2} to {3}", player, seqType, start, end);
        return true;
    }

    // Checks for a win by the player that passes through a specific cell. This is useful for incrementally checking for a win condition, as we don't have to do checks to unchanged parts of the board. Returns true if the player won, false otherwise
    public bool CheckForWinIncremental(byte player, Vector2Int cell, out Vector2Int start, out Vector2Int end, out string seqType)
    {
        int startX, startY, endX, endY;
        seqType = "";

        if (CheckHorizontalWin(player, cell.y, out startX, out endX))
        {
            seqType = SEQ_TYPE_HORIZONTAL;
            start = new Vector2Int(startX, cell.y);
            end = new Vector2Int(endX, cell.y);
        }
        else if (CheckVerticalWin(player, cell.x, out startY, out endY))
        {
            seqType = SEQ_TYPE_VERTICAL;
            start = new Vector2Int(cell.x, startY);
            end = new Vector2Int(cell.x, endY);
        }
        else if (CheckDiagonalLeftWin(player, cell, out startX, out endY))
        {
            seqType = SEQ_TYPE_DIAGONAL_LEFT;
            start = new Vector2Int(startX, endY - (ConnectFour.SEQ_COUNT - 1));
            end = new Vector2Int(startX + (ConnectFour.SEQ_COUNT - 1), endY);
        }
        else if (CheckDiagonalRightWin(player, cell, out startX, out endY))
        {
            seqType = SEQ_TYPE_DIAGONAL_RIGHT;
            start = new Vector2Int(startX, endY - (ConnectFour.SEQ_COUNT - 1));
            end = new Vector2Int(startX - (ConnectFour.SEQ_COUNT - 1), endY);
        }
        else
        {
            //Debug.LogFormat("ConnectFour : CheckForWinIncremental - No wins found...\n-> player: {0}, cell: {1}", player, cell);
            start = end = new Vector2Int(-1, -1);
            return false;
        }

        //Debug.LogFormat("ConnectFour : CheckForWinIncremental - Player {0} wins! Used a {2} sequence, from {3} to {4}\n-> player: {0}, cell: {1}", player, cell, seqType, start, end);
        return true;
    }

    // Returns a heuristic score of the cell owned by playerId
    // The score is calculated depending on the amount of adjacent cells are occupied by our tokens (offensive/stacking moves) and the other players tokens (defensive/blocking moves) The scores are as follows:
    /*           
        - Adjacency to our tokens
        0 adjacent : +0 pts
        1 adjacent : +1 pts
        2 adjacent : +2 pts
        3 adjacent : +5 pts (aka we win next turn)

        - Adjacency to other players tokens
        0 adjacent : +0 pts
        1 adjacent : +1 pts (or 0 if we don't want to encourage the NPC to just place tokens close to the other player)
        2 adjacent : +2 pts
        3 adjacent : +5 pts (aka we lose next turn)

        // Note that we don't give ourselves 3 points for being adjacent to 3 tokens, because otherwise a move that puts us adjacent to 2 of ours and 2 of the other players tokens will then result in higher score than one that either win us the game, or saves us from losing the game.
    */
    public int GetHeuristicScore_Method1(byte playerId, Vector2Int cell)
    {
        int score = 0;

        if (board[cell.x, cell.y] != playerId)
        {
            Debug.LogError("ConnectFour : GetHeuristicScore - Cell is not owned by player " + playerId);
        }
        else
        {
            int offensiveScore = 0;
            int defensiveScore = 0;

            // Get the max adjacency count (the maximum amount of sequential tokens that are next to our placement, either horizontally, vertically or diagonally left/right)
            int maxAdjacentToPlayer = GetAdjacentCountMax(playerId, cell);
            int maxAdjacentToOther = GetAdjacentCountMax(NetworkManager.GetOtherPlayer(playerId), cell);

            // Add score for adjacent cells belonging to us
            if (maxAdjacentToPlayer == ConnectFour.SEQ_COUNT - 1)
                offensiveScore = 100;//ConnectFour.SEQ_COUNT + 1;
            else
                offensiveScore = maxAdjacentToPlayer;

            // Add score for adjacent cells belonging to the other player
            if (maxAdjacentToOther == ConnectFour.SEQ_COUNT - 1)
                defensiveScore = 100;//ConnectFour.SEQ_COUNT + 1;
            else// if (maxAdjacentToOther > 1)
                defensiveScore = maxAdjacentToOther;

            return Mathf.Max(offensiveScore * 2, defensiveScore);
        }

        return score;
    }

    static readonly int[] weights = new int[] { 0, 0, 1, 4, 100 };

    // Returns a heuristic score of the board as an entirity, using the winningPositions array of arrays.
    // http://blogs.skicelab.com/maurizio/connect-four.html
    public int GetHeuristicScore_Method2(byte computerPlayerId, byte playerId, bool isAiTurn)
    {
        int computerScore = 0;
        int playerScore = 0;

        // Loop through every possible winning sequence
        foreach (var sequence in winningSequences)
        {
            int computerCount = 0;
            int playerCount = 0;

            // Fetch the number of computer and player tokens in the sequence
            foreach (var coord in sequence)
            {
                if (board[coord.x, coord.y] == 0)
                    continue;
                else if (board[coord.x, coord.y] == computerPlayerId)
                    computerCount++;
                else if (board[coord.x, coord.y] == playerId)
                    playerCount++;
            }

            computerScore += weights[computerCount];
            playerScore += weights[playerCount];
        }

        return isAiTurn ? computerScore - playerScore : playerScore - computerScore;
    }

    // Gets the sum of the adjacent count for this cell. This is essentially the amount of tokens connected to this cell
    public int GetAdjacentCountSum(byte checkPlayer, Vector2Int cell)
    {
        return GetAdjacentCountArray(checkPlayer, cell).Sum();
    }

    // Gets the highest adjacent count for this cell. This is essentially the highest sequence of tokens that are connected to this cell
    public int GetAdjacentCountMax(byte checkPlayer, Vector2Int cell)
    {
        return GetAdjacentCountArray(checkPlayer, cell).Max();
    }

    // --- PRIVATE METHODS ---

    // Places a players disc is a specific space, overriding what was there (if anything)
    void PutDisc(byte player, int x, int y)
    {
        if (IsPlayerValid(player) && IsCoordInRange(x, y))
            board[x, y] = player;
        else
            Debug.LogError("ConnectFour : PutDisc - Player invalid or coordinate out of range!");
    }

    // Check for a horizontal win for a particular player across the entire board. Returns true if we found a win. startXIndex and endXIndex will be filled with the start and end x-indexes of the winning row, and row will be set to the y-index of the row
    bool CheckHorizontalWin(byte player, out int startXIndex, out int endXIndex, out int row)
    {
        /*
                0   1   2   3   4   5   6
            a   |---|---|---|---|---|---|->
            b   |---|---|---|---|---|---|->
            c   |---|---|---|---|---|---|->
            d   |---|---|---|---|---|---|->
            e   |---|---|---|---|---|---|->
            f   |---|---|---|---|---|---|->

            Outer loop is a-f, inner loop is 0-6
        */

        startXIndex = endXIndex = row = -1;

        // Check if the player value is valid
        if (player != PLAYER_ONE && player != PLAYER_TWO)
        {
            Debug.LogErrorFormat("ConnectFour : CheckHorizontalWin({0}) - Player value is invalid", player);
            return false;
        }

        // Starting from the top row, check each for a win by summing the sequential discs owned by the player
        for (int y = 0; y < BOARD_HEIGHT; y++)
        {
            if (CheckHorizontalWin(player, y, out startXIndex, out endXIndex))
            {
                row = y;
                return true;
            }
        }

        // If we're here, we didn't find a win for this player
        return false;
    }

    // Check for a horizontal line win for a particular player across a specific row. Used for incremental win state checking - and will not return true for wins that are not on the row. Returns true if we found a win. startXIndex and endXIndex will be filled with the start and end x-index if the row contained a winning sequence
    bool CheckHorizontalWin(byte player, int row, out int startIndex, out int endIndex)
    {
        startIndex = endIndex = -1;
        int sum = 0;

        // Step through each space in the row
        for (int x = 0; x < BOARD_WIDTH; x++)
        {
            // Skip this row, if we don't have enough spaces left to make a win
            if (sum + (BOARD_WIDTH - x) < SEQ_COUNT)
            {
                break;
            }

            // Increment the sum if this space is occupied by the player
            if (board[x, row] == player)
            {
                // Increment
                sum++;

                // Return with a win if the sum is equal to SEQ_COUNT (the player has 4 tiles in a row)
                if (sum == SEQ_COUNT)
                {
                    startIndex = x - (SEQ_COUNT - 1);
                    endIndex = x;

                    return true;
                }
            }

            // Zero the sum if the space is either free, or taken by the other player
            else sum = 0;
        }

        return false;
    }

    // Check for a vertical win for a particular player across the entire board. Returns true if we found a win. startYIndex and endYIndex will be filled with the start and end y-indexes of the winning column, and column will be set to the x-index of the column
    bool CheckVerticalWin(byte player, out int startYIndex, out int endYIndex, out int column)
    {
        /*
                0   1   2   3   4   5   6
            a   |   |   |   |   |   |   |
            b   |   |   |   |   |   |   |
            c   |   |   |   |   |   |   |
            d   |   |   |   |   |   |   |
            e   |   |   |   |   |   |   |
            f   |   |   |   |   |   |   |
                V   V   V   V   V   V   V

            Outer loop is 0-6, inner loop is a-f
        */

        startYIndex = endYIndex = column = -1;

        // Check if the player value is valid
        if (player != PLAYER_ONE && player != PLAYER_TWO)
        {
            Debug.LogErrorFormat("ConnectFour : CheckVerticalWin({0}) - Player value is invalid", player);
            return false;
        }

        // Starting from the left column, check each for a win by summing the sequential discs owned by the player
        for (int x = 0; x < BOARD_WIDTH; x++)
        {
            if (CheckVerticalWin(player, x, out startYIndex, out endYIndex))
            {
                column = x;
                return true;
            }
        }

        // If we're here, we didn't find a win for this player
        return false;
    }

    // Check for a vertical line win for a particular player across a specific column. Used for incremental win state checking - and will not return true for wins that are not in the column. Returns true if we found a win. startIndex and endIndex will be filled with the start and end y-index if the column contained a winning sequence
    bool CheckVerticalWin(byte player, int column, out int startIndex, out int endIndex)
    {
        startIndex = endIndex = -1;
        int sum = 0;

        // Step through each space in the column
        for (int y = 0; y < BOARD_HEIGHT; y++)
        {
            // Skip this column, if we don't have enough spaces left to make a win
            if (sum + (BOARD_HEIGHT - y) < SEQ_COUNT)
            {
                break;
            }

            // Increment the sum if this space is occupied by the player
            if (board[column, y] == player)
            {
                // Increment
                sum++;

                // Return with a win if the sum is equal to SEQ_COUNT (the player has 4 tiles in a row)
                if (sum == SEQ_COUNT)
                {
                    startIndex = y - (SEQ_COUNT - 1);
                    endIndex = y;

                    return true;
                }
            }

            // Zero the sum if the space is either free, or taken by the other player
            else sum = 0;
        }

        return false;
    }

    // Check for a left diagonal win for a particular player across the entire board. Returns true if we found a win. startXIndex and endYIndex will be filled with the start x-index and end y-index of the winning row
    bool CheckDiagonalLeftWin(byte player, out int startXIndex, out int endYIndex)
    {
        /*
            A diagonal left win is one where the y index is stepped down from left to right. Loop starts at c0, and moves to a0, then across to a3. We skip the diagonal lines marked with 'x', as it is impossible to win a diagonal left win in those areas.

                0   1   2   3   4   5   6
            a   -   -   -   >   x   x   x
            b   |   \   \   \   \   x   x
            c   |   \   \   \   \   \   x
            d   x   \   \   \   \   \   \
            e   x   x   \   \   \   \   \
            f   x   x   x   \   \   \   \
        */

        startXIndex = endYIndex = -1;

        int startX = 0;
        int startY = BOARD_HEIGHT - SEQ_COUNT;

        while (startX <= BOARD_WIDTH - SEQ_COUNT)
        {
            if (CheckDiagonalLeftWin(player, new Vector2Int(startX, startY), out startXIndex, out endYIndex))
                return true;

            // Only increment startX when we're at y=0
            if (startY == 0) startX++;

            // Decrement startY if it's > 0
            if (startY > 0) startY--;
        }

        return false;
    }

    // Check for a left diagonal win for a particular player on a specific starting point. Used for incremental win state checking - and will not return true for wins that are not in the left diagonal line. Returns true if we found a sequence. startXIndex and endYIndex will be filled with the start x-index and end y-index of the winning sequence
    bool CheckDiagonalLeftWin(byte player, Vector2Int cell, out int startXIndex, out int endYIndex)
    {
        startXIndex = endYIndex = -1;

        // Convert the passed cell coordinate to a coordinate which starts on beginning of the diagonal line, if it isn't already
        if (cell.x > 0 && cell.y > 0)
        {
            // Move to the corner of the line by determining which axis is closer to the top left
            if (cell.x < cell.y)
            {
                cell.y -= cell.x;
                cell.x = 0;
            }
            else
            {
                cell.x -= cell.y;
                cell.y = 0;
            }  
        }

        // Return false if we're on a diagonal line where it is impossible to achieve a sequence
        if (cell.x > BOARD_WIDTH - SEQ_COUNT || cell.y > BOARD_HEIGHT - SEQ_COUNT)
            return false;

        int sum = 0;

        // Test the diagonal line starting at startX and startY
        for (int x = cell.x, y = cell.y; x < BOARD_WIDTH && y < BOARD_HEIGHT; x++, y++)
        {
            // Skip this left diagonal row, if we don't have enough spaces left to make a win
            if (sum + (BOARD_HEIGHT - y) < SEQ_COUNT || sum + (BOARD_WIDTH - x) < SEQ_COUNT)
                break;

            // Increment the sum if this space is occupied by the player
            if (board[x, y] == player)
            {
                // Increment
                sum++;

                // Return with a win if the sum is equal to SEQ_COUNT (the player has 4 tiles in a row)
                if (sum == SEQ_COUNT)
                {
                    startXIndex = x - (SEQ_COUNT - 1);
                    endYIndex = y;

                    return true;
                }
            }

            // Zero the sum if the space is either free, or taken by the other player
            else sum = 0;
        }

        return false;
    }

    // Check for a right diagonal win for a particular player across the entire board. Returns true if we found a win. startXIndex and endYIndex will be filled with the start x-index and end y-index of the winning row
    bool CheckDiagonalRightWin(byte player, out int startXIndex, out int endYIndex)
    {
        /*
            A diagonal right win is one where the y index is stepped down from right to left. The loop starts at c6, and moves to a6, then across to a3. We skip the diagonal lines marked with 'x', as it is impossible to win a diagonal right win in those areas.
            
                 0   1   2   3   4   5   6
            y0   x   x   x   <   -   -   -
            y1   x   x   /   /   /   /   |
            y2   x   /   /   /   /   /   |
            y3   /   /   /   /   /   /   x
            y4   /   /   /   /   /   x   x
            y5   /   /   /   /   x   x   x
        */

        startXIndex = endYIndex = -1;

        int startX = BOARD_WIDTH - 1;
        int startY = BOARD_HEIGHT - SEQ_COUNT;

        while (startX >= BOARD_WIDTH - SEQ_COUNT)
        {
            if (CheckDiagonalRightWin(player, new Vector2Int(startX, startY), out startXIndex, out endYIndex))
                return true;

            // Only decrement startX when we're at y=0
            if (startY == 0) startX--;

            // Decrement startY if it's > 0
            if (startY > 0) startY--;
        }

        return false;
    }

    // Check for a right diagonal win for a particular player on a specific starting point. Used for incremental win state checking - and will not return true for wins that are not in the right diagonal line. Returns true if we found a sequence. startXIndex and endYIndex will be filled with the start x-index and end y-index of the winning sequence
    bool CheckDiagonalRightWin(byte player, Vector2Int cell, out int startXIndex, out int endYIndex)
    {
        startXIndex = endYIndex = -1;

        int widthMinusOne = BOARD_WIDTH - 1;

        // Convert the passed cell coordinate to a coordinate which starts on beginning of the diagonal line, if it isn't already
        if (cell.x < widthMinusOne && cell.y > 0)
        {
            // Move to the corner of the line by determining which axis is closer to the top right
            if ((widthMinusOne - cell.x) < cell.y)
            {
                cell.y -= (widthMinusOne - cell.x);
                cell.x = widthMinusOne;
            }
            else
            {
                cell.x += cell.y;
                cell.y = 0;
            }
        }

        // Return false if we're on a diagonal line where it is impossible to achieve a sequence
        if (cell.x < BOARD_WIDTH - SEQ_COUNT || cell.y > BOARD_HEIGHT - SEQ_COUNT)
            return false;

        int sum = 0;

        // Test the diagonal line starting at cell.x and cell.y
        for (int x = cell.x, y = cell.y; x >= 0 && y < BOARD_HEIGHT; x--, y++)
        {
            // Skip this right diagonal row, if we don't have enough spaces left to make a win
            if (sum + (BOARD_HEIGHT - y) < SEQ_COUNT || sum + (x + 1) < SEQ_COUNT)
                break;

            // Increment the sum if this space is occupied by the player
            if (board[x, y] == player)
            {
                // Increment
                sum++;

                // Return with a win if the sum is equal to SEQ_COUNT (the player has 4 tiles in a row)
                if (sum == SEQ_COUNT)
                {
                    startXIndex = x + (SEQ_COUNT - 1);
                    endYIndex = y;

                    return true;
                }
            }

            // Zero the sum if the space is either free, or taken by the other player
            else sum = 0;
        }

        return false;
    }

    int[] GetAdjacentCountArray(byte checkPlayer, Vector2Int cell)
    {
        int horizontalCount = GetHorizontalAdjacentCount(checkPlayer, cell);
        int verticalCount = GetVerticalAdjacentCount(checkPlayer, cell);
        int diagonalLeftCount = GetDiagonalLeftAdjacentCount(checkPlayer, cell);
        int diagonalRightCount = GetDiagonalRightAdjacentCount(checkPlayer, cell);

        return (new int[] { horizontalCount, verticalCount, diagonalLeftCount, diagonalRightCount });
    }

    // Gets the number of spaces in a sequence that are occupied by <checkPlayer> on the left or the right side of a specific <cell>
    // This function is used by the NPC player to determine ideal offensive and defensive moves
    // This includes the following configurations: (where o is the start point, and x are occupied cells)
    // o x x x, x o x x, x x o x, x x x o
    int GetHorizontalAdjacentCount(byte checkPlayer, Vector2Int cell)
    {
        return GetAdjacentCount(checkPlayer, cell, new Vector2Int(1, 0));
    }

    // Gets the number of spaces in a sequence that are occupied by <checkPlayer> above or below a specific <cell>
    // This function is used by the NPC player to determine ideal offensive and defensive moves
    int GetVerticalAdjacentCount(byte checkPlayer, Vector2Int cell)
    {
        return GetAdjacentCount(checkPlayer, cell, new Vector2Int(0, 1));
    }

    // Gets the number of spaces in a sequence that are occupied by <checkPlayer> in a diagonal-right line from <cell>
    // This function is used by the NPC player to determine ideal offensive and defensive moves
    int GetDiagonalRightAdjacentCount(byte checkPlayer, Vector2Int cell)
    {
        return GetAdjacentCount(checkPlayer, cell, new Vector2Int(-1, -1));
    }

    // Gets the number of spaces in a sequence that are occupied by <checkPlayer> in a diagonal-left line from <cell>
    // This function is used by the NPC player to determine ideal offensive and defensive moves
    int GetDiagonalLeftAdjacentCount(byte checkPlayer, Vector2Int cell)
    {
        return GetAdjacentCount(checkPlayer, cell, new Vector2Int(-1, 1));
    }

    // Gets the number of spaces in a sequence that are occupied by <checkPlayer> adjacent to (but not including) the <cell>
    // This function is used by the NPC player to determine ideal offensive and defensive moves
    // maxOffset is the maximum amount of spaces we offset from the cell. This is done to avoid checking for when we don't need to know about cells that far away (sequential cells more than or equal to SEQ_COUNT are a win anyway)
    // offsetScale is used to control how the check offset changes each step. For example, if the offsetScale.x is -1, every step the check will move one cell to the left. If it is 0, it will remain unchanged
    int GetAdjacentCount(byte checkPlayer, Vector2Int cell, Vector2Int offsetScale, int maxOffset = SEQ_COUNT - 1)
    {
        int count = 0;

        if (!IsCoordInRange(cell) || !IsPlayerValid(checkPlayer))
            return count;

        if (offsetScale.x > 1 || offsetScale.x < -1 || offsetScale.y > 1 || offsetScale.y < -1)
        {
            Debug.LogError("ConnectFour : GetAdjacentCount - Offset cannot be more than 1 or less than -1");
            return count;
        }

        if (offsetScale.x == 0 && offsetScale.y == 0)
        {
            Debug.LogError("ConnectFour : GetAdjacentCount - Offset x and y cannot both be 0");
            return count;
        }

        bool doPositiveCheck = true;
        bool doNegativeCheck = true;

        int x = cell.x; int y = cell.y;
        
        for (int offset = 1; offset <= maxOffset; offset++)
        {
            if (doPositiveCheck)
            {
                x = cell.x + offset * offsetScale.x;
                y = cell.y + offset * offsetScale.y;

                //Debug.LogFormat("ConnectFour : GetAdjacentCount - Cell x{0}y{1}, Checking for player {2} x{3}y{4} (offsetScale is {5}, offset is {6}", cell.x, cell.y, checkPlayer, x, y, offsetScale, offset);

                // If the space <offset> the cell is valid, and it contains the checkPlayer, increment count by 1
                if (IsColumnInRange(x) && IsRowInRange(y) && board[x, y] == checkPlayer)
                    count++;
                else
                    doPositiveCheck = false;
            }

            if (doNegativeCheck)
            {
                x = cell.x - offset * offsetScale.x;
                y = cell.y - offset * offsetScale.y;

                // If the space <offset> the cell is valid, and it contains the checkPlayer, increment count by 1
                if (IsColumnInRange(x) && IsRowInRange(y) && board[x, y] == checkPlayer)
                    count++;
                else
                    doNegativeCheck = false;
            }

            // Return the current count if we either hit the edge of the board on both sides, or got to an empty cell or one that was not occupied by the checkPlayer
            if (!doPositiveCheck && !doNegativeCheck)
                break;
        }

        return count;
    }

    static void AddSequence(Vector2Int origin, Vector2Int offset)
    {
        var sequence = new Vector2Int[4];

        // Create sequence at this index for each position
        for (int i = 0; i < SEQ_COUNT; i++)
        {
            sequence[i] = new Vector2Int(origin.x + (offset.x * i),
                                         origin.y + (offset.y * i));
        }

        winningSequences.Add(sequence);
    }
    
    static void CalculatetWinningSequences()
    {
        /*
        System.Action<Vector2Int, Vector2Int> AddSequence = (origin, offset) =>
        {
            var sequence = new Vector2Int[4];

            // Create sequence at this index for each position
            for (int i = 0; i < SEQ_COUNT; i++)
            {
                sequence[i] = new Vector2Int(origin.x + (offset.x * i),
                                             origin.y + (offset.y * i));
            }

            // !
            var c4 = new ConnectFour();
            c4.board = ConnectFour.EmptyBoard;
            
            for (int i = 0; i < sequence.Length; i++)
                c4.board[sequence[i].x, sequence[i].x] = 1;

            Debug.Log(c4.ToString());

            winningSequences.Add(sequence);
        };
        */

        // Calculate all horizontal winning sequences
        for (int y = 0; y < BOARD_HEIGHT; y++)
        {
            // Step through each space in the row
            for (int x = 0; x < BOARD_WIDTH; x++)
            {
                // Skip this row, if its impossible to make a sequence of 4
                if (x > BOARD_WIDTH - SEQ_COUNT)
                    break;

                /*
                // Create sequence at this index for each position
                for (int i = 0; i < SEQ_COUNT; i++)
                {
                    winningSequences[index][i] = new Vector2Int(x + i, y);
                    index++;
                }
                */
                AddSequence(new Vector2Int(x, y), new Vector2Int(+1, 0));
            }
        }

        // Calculate all vertical winning sequences
        for (int x = 0; x < BOARD_WIDTH; x++)
        {
            // Step through each space in the column
            for (int y = 0; y < BOARD_HEIGHT; y++)
            {
                // Skip this column, if its impossible to make a sequence of 4
                if (y > BOARD_HEIGHT - SEQ_COUNT)
                    break;

                /*
                // Create sequence at this index for each position
                for (int i = 0; i < SEQ_COUNT; i++)
                {
                    winningSequences[index, i] = new Vector2Int(x, y + i);
                    index++;
                }
                */

                AddSequence(new Vector2Int(x, y), new Vector2Int(0, +1));
            }
        }

        // Calculate all diagonal-left (\) sequences along the left side of the board
        for (int startY = BOARD_HEIGHT; startY >= 0; startY--)
        {
            // Step through each space in the diagonal-left line
            for (int x = 0, y = startY; x < BOARD_WIDTH && y < BOARD_HEIGHT; x++, y++)
            {
                // Skip this sequence, if its impossible to make a sequence of four here
                if (x > BOARD_WIDTH - SEQ_COUNT || y > BOARD_HEIGHT - SEQ_COUNT)
                    break;

                /*
                // Create sequence at this index for each position
                for (int i = 0; i < SEQ_COUNT; i++)
                {
                    winningSequences[index, i] = new Vector2Int(x + i, y + i);
                    index++;
                }
                */
                AddSequence(new Vector2Int(x, y), new Vector2Int(+1, +1));
            }
        }

        // Calculate all diagonal-left (\) sequences along the top of the board (discluding the line starting at x0y0, as it was covered by the above)
        for (int startX = 1; startX < BOARD_WIDTH; startX++)
        {
            // Step through each space in the diagonal-left line
            for (int x = startX, y = 0; x < BOARD_WIDTH && y < BOARD_HEIGHT; x++, y++)
            {
                // Skip this sequence, if its impossible to make a sequence of four here
                if (x > BOARD_WIDTH - SEQ_COUNT || y > BOARD_HEIGHT - SEQ_COUNT)
                    break;

                /*
                // Create sequence at this index for each position
                for (int i = 0; i < SEQ_COUNT; i++)
                {
                    winningSequences[index, i] = new Vector2Int(x + i, y + i);
                    index++;
                }
                */
                AddSequence(new Vector2Int(x, y), new Vector2Int(+1, +1));
            }
        }

        // Calculate all diagonal-right (/) sequences along the top of the board
        for (int startX = 0; startX < BOARD_WIDTH; startX++)
        {
            // Step through each space in the diagonal-left line
            for (int x = startX, y = 0; x < BOARD_WIDTH && y < BOARD_HEIGHT; x--, y++)
            {
                // Skip this sequence, if its impossible to make a sequence of four here
                if (x < SEQ_COUNT - 1 || y > BOARD_HEIGHT - SEQ_COUNT)
                    break;

                /*
                // Create sequence at this index for each position
                for (int i = 0; i < SEQ_COUNT; i++)
                {
                    winningSequences[index, i] = new Vector2Int(x - i, y + i);
                    index++;
                }
                */

                AddSequence(new Vector2Int(x, y), new Vector2Int(-1, +1));
            }
        }

        // Calculate all diagonal-right (/) sequences along the right side of the board (discluding the line starting at x(BOARD_WIDTH)y0, as it was covered by the above)
        for (int startY = 1; startY < BOARD_HEIGHT; startY++)
        {
            // Step through each space in the diagonal-left line
            for (int x = BOARD_WIDTH - 1, y = startY; x >= 0 && y < BOARD_HEIGHT; x--, y++)
            {
                // Skip this left diagonal row, if its impossible to make a sequence of four here
                if (x < SEQ_COUNT - 1|| y > BOARD_HEIGHT - SEQ_COUNT)
                    break;

                /*
                // Create sequence at this index for each position
                for (int i = 0; i < SEQ_COUNT; i++)
                {
                    winningSequences[index, i] = new Vector2Int(x - i, y + i);
                    index++;
                }
                */

                AddSequence(new Vector2Int(x, y), new Vector2Int(-1, +1));
            }
        }
    }
}
