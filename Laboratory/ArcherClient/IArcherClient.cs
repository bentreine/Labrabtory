using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Laboratory.ArcherClient
{
    public interface IArcherClient
    {
        Task<int> CreateNewReview(CreateNewArcherReviewRequest archerRequest);

    }
}
