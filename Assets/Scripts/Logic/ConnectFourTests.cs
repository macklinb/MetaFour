using UnityEngine;
using System.Collections.Generic;

public class ConnectFourTests : MonoBehaviour
{
    ConnectFour connectFour;

    void Start()
    {
        connectFour = ConnectFour.Instance;
        DoHorizontalTests();
        DoVerticalTests();
        DoDiagonalLeftTests();
        DoDiagonalRightTests();
        DoRandomTests();
    }

    void DoHorizontalTests()
    {
        for (int y = 0; y < ConnectFour.BOARD_HEIGHT; y++)
        {
            // Create a board where we shift the four occupied cells right each loop until they no longer fit
            for (int startX = 0; startX <= ConnectFour.BOARD_WIDTH - ConnectFour.SEQ_COUNT; startX++)
            {
                byte[,] board = new byte[ConnectFour.BOARD_WIDTH, ConnectFour.BOARD_HEIGHT];
                
                for (int x = 0; x < ConnectFour.SEQ_COUNT; x++)
                    board[startX + x, y] = 1;

                // Set the board directly
                connectFour.SetBoard(board);

                // Print the board
                connectFour.PrintBoard();

                // Try to solve the board in its entirity
                connectFour.CheckForWin(1);

                // Try to solve just the row
                connectFour.CheckForWinIncremental(1, new Vector2Int(0, y));
            }
        }
    }

    void DoVerticalTests()
    {
        for (int x = 0; x < ConnectFour.BOARD_WIDTH; x++)
        {
            // Create a board where we shift the four occupied cells down each loop until they no longer fit
            for (int startY = 0; startY <= ConnectFour.BOARD_HEIGHT - ConnectFour.SEQ_COUNT; startY++)
            {
                byte[,] board = new byte[ConnectFour.BOARD_WIDTH, ConnectFour.BOARD_HEIGHT];
                
                // Set a sequence of 4 within this column starting at startY and adding 1 to Y each time
                for (int y = 0; y < ConnectFour.SEQ_COUNT; y++)
                    board[x, startY + y] = 1;

                // Set the board directly
                connectFour.SetBoard(board);

                // Print the board
                connectFour.PrintBoard();

                // Try to solve the board in its entirity
                connectFour.CheckForWin(1);

                // Try to solve just the column
                connectFour.CheckForWinIncremental(1, new Vector2Int(x, 0));
            }
        }
    }

    void DoDiagonalLeftTests()
    {
        int startX = 0;
        int startY = ConnectFour.BOARD_HEIGHT - ConnectFour.SEQ_COUNT;

        while (startX <= ConnectFour.BOARD_WIDTH - ConnectFour.SEQ_COUNT)
        {
            for (int x = startX, y = startY; x <= ConnectFour.BOARD_WIDTH - ConnectFour.SEQ_COUNT && y <= ConnectFour.BOARD_HEIGHT - ConnectFour.SEQ_COUNT; x++, y++)
            {
                byte[,] board = new byte[ConnectFour.BOARD_WIDTH, ConnectFour.BOARD_HEIGHT];

                // Set 4 sequentially, starting at X and Y - moving right by 1 and down by 1 every loop
                for (int innerX = 0, innerY = 0;
                    innerX < ConnectFour.SEQ_COUNT && innerY < ConnectFour.SEQ_COUNT;
                    innerX++, innerY++)
                    board[x + innerX, y + innerY] = 1;

                // Set the board directly
                connectFour.SetBoard(board);

                // Print the board
                connectFour.PrintBoard();

                // Try to solve the board in its entirity
                connectFour.CheckForWin(1);

                // Try to solve just the diagonal line
                connectFour.CheckForWinIncremental(1, new Vector2Int(x, y));
            }

            // Only increment startX when we're at y=0
            if (startY == 0) startX++;

            // Decrement startY if it's > 0
            if (startY > 0) startY--;
        }
    }

    void DoDiagonalRightTests()
    {
        int startX = ConnectFour.BOARD_WIDTH - 1;
        int startY = ConnectFour.BOARD_HEIGHT - ConnectFour.SEQ_COUNT;

        while (startX >= ConnectFour.SEQ_COUNT - 1)
        {
            for (int x = startX, y = startY; x >= ConnectFour.SEQ_COUNT - 1 && y <= ConnectFour.BOARD_HEIGHT - ConnectFour.SEQ_COUNT; x--, y++)
            {
                byte[,] board = new byte[ConnectFour.BOARD_WIDTH, ConnectFour.BOARD_HEIGHT];

                // Set 4 sequentially, starting at X and Y - moving right by 1 and down by 1 every loop
                for (int innerX = 0, innerY = 0;
                    innerX < ConnectFour.SEQ_COUNT && innerY < ConnectFour.SEQ_COUNT;
                    innerX++, innerY++)
                    board[x - innerX, y + innerY] = 1;

                // Set the board directly
                connectFour.SetBoard(board);

                // Print the board
                connectFour.PrintBoard();

                // Try to solve the board in its entirity
                connectFour.CheckForWin(1);

                // Try to solve just the diagonal line
                connectFour.CheckForWinIncremental(1, new Vector2Int(x, y));
            }

            // Only increment startX when we're at y=0
            if (startY == 0) startX--;

            // Decrement startY if it's > 0
            if (startY > 0) startY--;
        }
    }

    void DoRandomTests()
    {
        // Generate 10,000 random boards
        for (int i = 0; i < 10000; i++)
        {
            byte[,] board = new byte[ConnectFour.BOARD_WIDTH, ConnectFour.BOARD_HEIGHT];

            var filled = new List<Vector2Int>();
            int halfAmt = (ConnectFour.BOARD_HEIGHT * ConnectFour.BOARD_WIDTH) / 2;

            // Fill half of the board at random with 1's
            while (filled.Count != halfAmt)
            {
                var coords = new Vector2Int(Random.Range(0, ConnectFour.BOARD_WIDTH),
                                            Random.Range(0, ConnectFour.BOARD_HEIGHT));

                if (filled.Contains(coords))
                    continue;
                else
                {
                    board[coords.x, coords.y] = 1;
                    filled.Add(coords);
                }
            }

            // Set the board directly
            connectFour.SetBoard(board);

            // Print the board
            connectFour.PrintBoard();

            // Try to solve the board
            connectFour.CheckForWin(1);
        }
    }
}